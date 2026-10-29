using HarmonyLib;
using Il2CppHotfixSystem.Core;
using Il2CppAssets.Scripts.Database;

namespace CustomAlbums.Android;

[HarmonyPatch(typeof(HotfixOrchestrator), nameof(HotfixOrchestrator.StartSingleTrackDownloadAsync))]
internal static class LocalTrackDownload
{
    private static bool Prefix(MusicInfo musicInfo, Il2CppSystem.Action onComplete, ref Il2CppSystem.Threading.Tasks.Task __result)
    {
        var album = CustomContentAvailability.Match(musicInfo);
        if (album == null)
            return true;
        try
        {
            ResourceHook.PrepareLocalPlayback(album);
            __result = Il2CppSystem.Threading.Tasks.Task.CompletedTask;
            onComplete?.Invoke();
        }
        catch (Exception ex)
        {
            Mod.Log.Error("CA_LOCAL_TRACK_PREPARATION_FAILED uid=" + album.Uid + " " + ex);
            var error = new Il2CppSystem.IO.IOException("Local custom track validation or audio decoding failed: " + ex.Message);
            __result = Il2CppSystem.Threading.Tasks.Task.FromException(error);
        }

        return false;
    }
}
