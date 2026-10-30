using CustomAlbums.Core;
using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore.Managers;
using Il2CppAssets.Scripts.UI.Panels;
using Il2CppFormulaBase;
using Il2CppGameLogic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Minkyu.Mods;

internal static class ChartVideo
{
    private static NativeVideo player;
    private static Camera videoCamera, gameCamera;
    private static CameraClearFlags originalClear;
    private static int originalMask;
    private static float opacity;
    private static bool playRequested;
    private static bool started, visible, paused, reportedReady;
    private static float preparingAt, nextCorrection;
    private static Task<(string File, CinemaSettings Settings)> preparing;
    private static readonly Dictionary<int, (Renderer Renderer, bool Enabled)> scenery = new();

    internal static void Prepare()
    {
        Stop();
        if (!Mod.On("Media.Video")) return;
        var stage = GlobalDataBase.dbBattleStage;
        var uid = stage.musicUid;
        var difficulty = stage.selectedDifficulty;
        if (CustomAlbums.Android.Mod.GetAlbumPath(uid) == null) return;
        var cache = Path.Combine(Application.temporaryCachePath, "MinkyuVideo");
        preparing = Task.Run(() => CustomAlbums.Android.Mod.ReadAlbum(uid, archive =>
        {
            var cinema = CinemaSettings.Read(archive);
            if (cinema == null || !cinema.Difficulties.Contains(difficulty)) return default;
            var file = Extract(archive, cinema, cache);
            return (file, cinema);
        }));
    }

    private static string Extract(MdmArchive archive, CinemaSettings cinema, string cache)
    {
        Directory.CreateDirectory(cache);
        var file = Path.Combine(cache, archive.Id + ".mp4");
        if (!File.Exists(file))
        {
            var pending = file + ".pending";
            if (File.Exists(pending)) File.Delete(pending);
            archive.ExtractAsset(cinema.FileName, pending);
            File.Move(pending, file);
            foreach (var old in Directory.EnumerateFiles(cache, "*.mp4"))
                if (old != file) File.Delete(old);
        }
        return file;
    }

    private static void CreatePlayer(string file, CinemaSettings cinema)
    {
        // Draw before Camera_2D, which clears color at depth -1.
        gameCamera = Camera.allCameras.FirstOrDefault(c => c.name == "Camera_2D");
        if (gameCamera == null) throw new InvalidOperationException("Cinema game camera unavailable");
        originalClear = gameCamera.clearFlags;
        originalMask = gameCamera.cullingMask;
        opacity = cinema.Opacity;
        var cameraObject = new GameObject("Minkyu.Cinema");
        videoCamera = cameraObject.AddComponent<Camera>();
        videoCamera.CopyFrom(gameCamera);
        cameraObject.transform.position = gameCamera.transform.position;
        cameraObject.transform.rotation = gameCamera.transform.rotation;
        videoCamera.enabled = false;
        videoCamera.depth = Camera.allCameras.Where(c => c != videoCamera).Min(c => c.depth) - 1;
        videoCamera.cullingMask = 1 << 31;
        videoCamera.clearFlags = CameraClearFlags.SolidColor;
        videoCamera.backgroundColor = Color.black;
        player = new NativeVideo(cameraObject);
        player.Configure(file, videoCamera, cinema.Opacity);
        preparingAt = Time.realtimeSinceStartup;
        player.Prepare();
        Mod.Log.Msg("Custom video preparing");
    }

    internal static void Start()
    {
        if (preparing == null && player == null) Prepare();
        started = true;
        paused = false;
        nextCorrection = 0;
    }

    internal static void Tick()
    {
        try { UpdatePlayer(); }
        catch
        {
            Stop();
            throw;
        }
    }

    private static void UpdatePlayer()
    {
        // Wait for the new scene before attaching to its camera.
        if (started && preparing != null && preparing.IsCompleted)
        {
            var done = preparing;
            preparing = null;
            var result = done.GetAwaiter().GetResult();
            if (result.File != null && Mod.On("Media.Video")) CreatePlayer(result.File, result.Settings);
        }
        if (player == null) return;
        if (!Mod.On("Media.Video")) { Stop(); return; }
        if (!player.isPrepared)
        {
            if (Time.realtimeSinceStartup - preparingAt > 20)
            {
                Mod.Log.Warning("Custom video preparation timed out");
                Stop();
            }
            return;
        }
        if (!reportedReady)
        {
            reportedReady = true;
            Mod.Log.Msg("Custom video ready: " + player.width + "x" + player.height);
        }
        var stage = StageBattleComponent.instance;
        if (!started || stage == null) return;
        if (!playRequested)
        {
            playRequested = true;
            if (player.canSetTime) player.time = Math.Max(0, stage.timeFromMusicStart);
            player.Play();
        }
        var texture = visible ? null : player.Texture;
        if (!visible && texture != null)
        {
            // Video composition shaders are stripped; draw the texture with the UI shader.
            var canvasObject = new GameObject("Minkyu.VideoCanvas", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(), Il2CppInterop.Runtime.Il2CppType.Of<Canvas>());
            canvasObject.layer = 31;
            canvasObject.transform.SetParent(videoCamera.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = videoCamera;
            canvas.planeDistance = Math.Max(1, videoCamera.nearClipPlane + 1);
            var imageObject = new GameObject("Video", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(), Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(), Il2CppInterop.Runtime.Il2CppType.Of<RawImage>());
            imageObject.layer = 31;
            imageObject.transform.SetParent(canvasObject.transform, false);
            var image = imageObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.texture = texture;
            image.color = new Color(1, 1, 1, opacity);
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            var ratio = (float)player.width / player.height / videoCamera.aspect;
            image.uvRect = ratio < 1 ? new Rect(0, (1 - ratio) / 2, 1, ratio) : new Rect((1 - 1 / ratio) / 2, 0, 1 / ratio, 1);
            visible = true;
            videoCamera.enabled = true;
            gameCamera.clearFlags = CameraClearFlags.Depth;
            gameCamera.cullingMask = originalMask & ~(1 << 31);
            HideScenery();
            Mod.Log.Msg("Custom video texture visible");
        }
        if (stage.isPause)
        {
            if (!paused) { player.Pause(); paused = true; }
            return;
        }
        if (paused)
        {
            paused = false;
            if (player.canSetTime) player.time = Math.Max(0, stage.timeFromMusicStart);
            player.Play();
        }
        if (Time.unscaledTime < nextCorrection || !player.isPlaying) return;
        nextCorrection = Time.unscaledTime + .1f;
        // Correct the video against the music clock.
        var drift = player.time - Math.Max(0, stage.timeFromMusicStart);
        if (Math.Abs(drift) > .3 && player.canSetTime) player.time = Math.Max(0, stage.timeFromMusicStart);
        else if (player.canSetPlaybackSpeed) player.playbackSpeed = Mathf.Clamp(1 - (float)drift * .1f, .95f, 1.05f);
    }

    internal static void HideScenery()
    {
        if (!visible || player == null) return;
        var scene = GameGlobal.gGameMusicScene;
        if (scene?.scenes != null)
        {
            foreach (var root in scene.scenes) Hide(root);
        }
        else if (scene?.scene != null) Hide(scene.scene);
    }

    internal static void Fever(FeverEffectManager fever)
    {
        if (visible && fever.m_Background != null) Hide(fever.m_Background);
    }

    private static void Hide(GameObject root)
    {
        if (root == null) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            scenery.TryAdd(renderer.GetInstanceID(), (renderer, renderer.enabled));
            renderer.enabled = false;
        }
    }

    internal static void Stop()
    {
        preparing = null;
        if (player != null) player.Stop();
        player = null;
        if (gameCamera != null)
        {
            gameCamera.clearFlags = originalClear;
            gameCamera.cullingMask = originalMask;
        }
        if (videoCamera != null) Object.Destroy(videoCamera.gameObject);
        gameCamera = videoCamera = null;
        foreach (var state in scenery.Values) if (state.Renderer != null) state.Renderer.enabled = state.Enabled;
        scenery.Clear();
        started = visible = paused = reportedReady = playRequested = false;
    }
}

[HarmonyPatch(typeof(StageBattleComponent), nameof(StageBattleComponent.Load))]
internal static class VideoLoad
{
    private static void Postfix() => Mod.Guard(() =>
    {
        try { ChartVideo.Prepare(); }
        catch { ChartVideo.Stop(); throw; }
    });
}

[HarmonyPatch(typeof(PnlBattle), nameof(PnlBattle.GameStart))]
internal static class VideoStart
{
    private static void Postfix() => Mod.Guard(ChartVideo.Start);
}

[HarmonyPatch(typeof(StageBattleComponent), nameof(StageBattleComponent.GameRestart))]
internal static class VideoRestart
{
    // Stop before the scene destroys the native player; recreate it at GameStart.
    private static void Prefix() => Mod.Guard(ChartVideo.Stop);
}

[HarmonyPatch(typeof(StageBattleComponent), nameof(StageBattleComponent.Exit))]
internal static class VideoExit
{
    private static void Prefix() => Mod.Guard(ChartVideo.Stop);
}

[HarmonyPatch(typeof(StatisticsManager), nameof(StatisticsManager.OnBattleEnd))]
internal static class VideoResult
{
    private static void Prefix() => Mod.Guard(ChartVideo.Stop);
}

[HarmonyPatch(typeof(SceneChangeController), nameof(SceneChangeController.OnControllerStart))]
internal static class VideoScene
{
    private static void Postfix() => Mod.Guard(ChartVideo.HideScenery);
}

[HarmonyPatch(typeof(FeverEffectManager), nameof(FeverEffectManager.ActivateFever))]
internal static class VideoFever
{
    private static void Postfix(FeverEffectManager __instance) => Mod.Guard(() => ChartVideo.Fever(__instance));
}
