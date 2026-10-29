using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.Database.DataClass;
using Il2CppAssets.Scripts.PeroTools.Commons;
using Il2CppAssets.Scripts.PeroTools.Managers;

namespace CustomAlbums.Android;

[HarmonyPatch(typeof(MusicTagManager), nameof(MusicTagManager.InitAlbumTagInfo))]
internal static class CustomAlbumTag
{
    private static void Postfix()
    {
        try
        {
            var sort = GlobalDataBase.dbMusicTag.m_AlbumTagsSort;
            if (sort.Contains(999))
                return;
            var languages = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
            foreach (var language in new[] { "English", "ChineseS", "ChineseT", "Japanese", "Korean" }
            )
                languages.Add(language, "Custom Albums");
            var uids = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var album in AlbumCatalog.Albums)
                uids.Add(album.Uid);
            var custom = new DBConfigCustomTags.CustomTagInfo
            {
                tag_name = languages,
                tag_picture = "",
                icon_name = "IconCustomAlbums",
                music_list = uids
            };
            var tag = new AlbumTagInfo
            {
                name = "Custom Albums",
                tagUid = "tag-custom-albums",
                iconName = "IconCustomAlbums"
            };
            tag.InitCustomTagInfo(custom);
            sort.Insert(Math.Max(0, sort.Count - 4), 999);
            GlobalDataBase.dbMusicTag.AddAlbumTagData(999, tag);
        }
        catch (Exception ex)
        {
            Mod.Log.Error("CA_CUSTOM_SECTION_FAILED " + ex);
        }
    }
}

[HarmonyPatch(typeof(MusicTagManager), nameof(MusicTagManager.InitDatas))]
internal static class LimitSyntheticAlbumEnumeration
{
    private static void Postfix()
    {
        var albums = Singleton<ConfigManager>.instance.GetConfigObject<DBConfigAlbums>();
        var maximum = 0;
        foreach (var album in albums.GetAllDatas())
            if (album.uid != AlbumCatalog.PackageUid)
                maximum = Math.Max(maximum, album.albumUidIndex);
        // The synthetic album index must not extend enumeration of official resource bundles.
        albums.m_MaxAlbumUid = maximum;
    }
}
