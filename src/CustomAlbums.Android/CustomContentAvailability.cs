using HarmonyLib;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.Helpers;

namespace CustomAlbums.Android;

internal static class CustomContentAvailability
{
    public static AlbumRecord Match(Il2CppAssets.Scripts.Database.MusicInfo selected)
    {
        if (!Mod.Ready || AlbumCatalog.Albums.Count == 0 || selected == null)
            return null;
        var album = AlbumCatalog.Albums.FirstOrDefault(a => a.Uid == selected.uid);
        return album != null && selected.music == album.AssetPrefix + "_music" && selected.noteJson == album.AssetPrefix + "_map" ? album : null;
    }

    public static bool IsSelectedRegisteredChart
    {
        get
        {
            if (!Mod.Ready || AlbumCatalog.Albums.Count == 0)
                return false;
            var selected = GlobalDataBase.dbMusicTag?.CurMusicInfo();
            if (selected == null)
                return false;
            return Match(selected) != null;
        }
    }
}

[HarmonyPatch(typeof(IAPHelper), nameof(IAPHelper.FastCheckUnlock), typeof(DBConfigAlbums.AlbumsInfo))]
internal static class SyntheticPackageAvailability
{
    private static bool Prefix(DBConfigAlbums.AlbumsInfo album, ref bool __result)
    {
        if (!Mod.Ready || AlbumCatalog.Albums.Count == 0)
            return true;
        if (album == null || album.uid != AlbumCatalog.PackageUid || album.jsonName != AlbumCatalog.AlbumJson || album.needPurchase || !album.free)
            return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(DataHelper), nameof(DataHelper.isSelectedMusicUnlock), MethodType.Getter)]
internal static class ImportedSongAvailability
{
    private static bool Prefix(ref bool __result)
    {
        if (!CustomContentAvailability.IsSelectedRegisteredChart)
            return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(DataHelper), nameof(DataHelper.isSelectedUnlockMaster), MethodType.Getter)]
internal static class ImportedDifficultyAvailability
{
    private static bool Prefix(ref bool __result)
    {
        if (!CustomContentAvailability.IsSelectedRegisteredChart)
            return true;
        __result = true;
        return false;
    }
}
