using MelonLoader;
using HarmonyLib;
using UnityEngine;

[assembly: MelonInfo(typeof(Minkyu.Mods.Mod), "Minkyu Mods", "0.1.0", "Minkyu contributors")]
namespace Minkyu.Mods;

public sealed class Mod : MelonMod
{
    internal static ModSettings Settings { get; private set; }
    internal static MelonLogger.Instance Log { get; private set; }
    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        var path = Path.Combine(Application.persistentDataPath, "Minkyu", "settings.json");
        try { Settings = new ModSettings(path); }
        catch (Exception ex)
        {
            Log.Error("Settings could not be loaded: " + ex.Message);
            if (File.Exists(path)) File.Move(path, path + ".rejected-" + DateTime.UtcNow.Ticks);
            Settings = new ModSettings(path);
        }
        CustomAlbums.Android.Mod.DialogueEnabled = On("Media.Dialogue");
        Settings.Changed += () => { CustomAlbums.Android.Mod.DialogueEnabled = On("Media.Dialogue"); InfoOverlay.RefreshSettings(); ScoreboardCharacters.RefreshSettings(); CustomLeaderboards.RefreshSettings(); };
        Log.Msg("Minkyu settings loaded");
    }
    public override void OnUpdate()
    {
        Guard(NativeSettings.Tick);
        InfoOverlay.Tick();
        Guard(CustomLeaderboards.Tick);
        Guard(ScoreboardCharacters.Tick);
        Guard(ChartVideo.Tick);
    }
    internal static bool On(string key) => Settings?.Get<bool>(key) == true;
    internal static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log?.Error(ex.ToString()); }
    }
}
