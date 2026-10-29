using HarmonyLib;
using Il2CppAccount;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore.Managers;

namespace CustomAlbums.Android;

internal static class CustomDataIsolation
{
    public static bool IsCustom(string uid) => uid?.StartsWith("999-", StringComparison.Ordinal) == true;
    public static bool InCustomBattle => Mod.Ready && AlbumCatalog.Albums.Count > 0 && IsCustom(GlobalDataBase.dbBattleStage?.musicUid);
}

[HarmonyPatch(typeof(GameAccountSystem), nameof(GameAccountSystem.SendToUrl))]
internal static class CustomScoreUploadGuard
{
    private static bool Prefix(string url, string method, Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object> datas)
    {
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            return true;
        if (datas != null)
            foreach (var key in new[] { "music_uid", "musicUid", "music_id" }
            )
                if (datas.ContainsKey(key) && CustomDataIsolation.IsCustom(datas[key]?.ToString()))
                    return false;
        if (!CustomDataIsolation.InCustomBattle)
            return true;
        return !(url?.Contains("leaderboard", StringComparison.OrdinalIgnoreCase) == true || url?.Contains("statistics", StringComparison.OrdinalIgnoreCase) == true || url?.Contains("high-score", StringComparison.OrdinalIgnoreCase) == true);
    }
}
