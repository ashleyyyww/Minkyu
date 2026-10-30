using HarmonyLib;
using Il2CppHotfixSystem.Core;
using Il2CppAssets.Scripts.Database;
using System.Collections;
using MelonLoader;

namespace CustomAlbums.Android;

[HarmonyPatch(typeof(HotfixOrchestrator), nameof(HotfixOrchestrator.StartSingleTrackDownloadAsync))]
internal static class LocalTrackDownload
{
    private static AlbumRecord decodingAlbum;
    private static System.Threading.Tasks.Task<AudioAssets.DecodedAudio> decoding;

    internal static System.Threading.Tasks.Task<AudioAssets.DecodedAudio> Decode(AlbumRecord album)
    {
        if (decodingAlbum == album && decoding != null) return decoding;
        decodingAlbum = album;
        return decoding = System.Threading.Tasks.Task.Run(() => AudioAssets.Decode(album, false));
    }

    internal static void Release(AlbumRecord album)
    {
        if (decodingAlbum != album) return;
        decodingAlbum = null;
        decoding = null;
    }

    private static bool Prefix(MusicInfo musicInfo, Il2CppSystem.Action onComplete, ref Il2CppSystem.Threading.Tasks.Task __result)
    {
        var album = CustomContentAvailability.Match(musicInfo);
        if (album == null)
            return true;
        try
        {
            album.CheckUnchanged();
            if (ResourceHook.HasMusic(album))
            {
                ResourceHook.PrepareLocalPlayback(album);
                __result = Il2CppSystem.Threading.Tasks.Task.CompletedTask;
                onComplete?.Invoke();
            }
            else
            {
                var completion = new Il2CppSystem.Threading.Tasks.TaskCompletionSource<Il2CppSystem.Object>();
                __result = completion.Task;
                var decoded = Decode(album);
                MelonCoroutines.Start(DecodeAndPublish(album, decoded, completion, onComplete));
            }
        }
        catch (Exception ex)
        {
            Mod.Log.Error("CA_LOCAL_TRACK_PREPARATION_FAILED uid=" + album.Uid + " " + ex);
            var error = new Il2CppSystem.IO.IOException("Local custom track validation or audio decoding failed: " + ex.Message);
            __result = Il2CppSystem.Threading.Tasks.Task.FromException(error);
        }

        return false;
    }

    private static IEnumerator DecodeAndPublish(AlbumRecord album, System.Threading.Tasks.Task<AudioAssets.DecodedAudio> decoded,
        Il2CppSystem.Threading.Tasks.TaskCompletionSource<Il2CppSystem.Object> completion, Il2CppSystem.Action onComplete)
    {
        // Decode on the worker; create Unity objects on the main thread.
        while (!decoded.IsCompleted) yield return null;
        try
        {
            ResourceHook.CompletePlayback(album, decoded.GetAwaiter().GetResult());
            onComplete?.Invoke();
            completion.SetResult(null);
        }
        catch (Exception ex)
        {
            Mod.Log.Error("CA_LOCAL_TRACK_PREPARATION_FAILED uid=" + album.Uid + " " + ex);
            completion.TrySetException(new Il2CppSystem.IO.IOException("Custom song preparation failed: " + ex.Message));
        }
        finally { Release(album); }
    }
}

[HarmonyPatch(typeof(Il2Cpp.PnlPreparation), nameof(Il2Cpp.PnlPreparation.OnEnable))]
internal static class CustomAudioPreload
{
    private static void Postfix()
    {
        var album = CustomContentAvailability.Match(GlobalDataBase.dbMusicTag?.CurMusicInfo());
        if (album != null && !ResourceHook.HasMusic(album)) LocalTrackDownload.Decode(album);
    }
}

[HarmonyPatch(typeof(Il2Cpp.PnlPreparation), nameof(Il2Cpp.PnlPreparation.OnBattleStart))]
internal static class CustomAudioStart
{
    private static bool waiting;

    private static bool Prefix(Il2Cpp.PnlPreparation __instance)
    {
        var album = CustomContentAvailability.Match(GlobalDataBase.dbMusicTag?.CurMusicInfo());
        if (album == null || ResourceHook.HasMusic(album)) return true;
        if (!waiting)
        {
            waiting = true;
            MelonCoroutines.Start(WaitForAudio(__instance, album, LocalTrackDownload.Decode(album)));
        }
        return false;
    }

    private static IEnumerator WaitForAudio(Il2Cpp.PnlPreparation panel, AlbumRecord album,
        System.Threading.Tasks.Task<AudioAssets.DecodedAudio> decoded)
    {
        while (!decoded.IsCompleted) yield return null;
        try
        {
            if (panel == null || !panel.gameObject.activeInHierarchy || GlobalDataBase.dbMusicTag.CurMusicInfo()?.uid != album.Uid) yield break;
            ResourceHook.CompletePlayback(album, decoded.GetAwaiter().GetResult());
            panel.OnBattleStart();
        }
        catch (Exception ex)
        {
            Mod.Log.Error("CA_LOCAL_TRACK_PREPARATION_FAILED uid=" + album.Uid + " " + ex);
        }
        finally
        {
            waiting = false;
            LocalTrackDownload.Release(album);
        }
    }
}
