using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppAssets.Scripts.PeroTools.Commons;
using Il2CppAssets.Scripts.PeroTools.Managers;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Il2CppPeroTools2.Resources;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomAlbums.Android;

internal static class ResourceHook
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LoadDelegate(IntPtr self, IntPtr name, IntPtr method);
    private static readonly LoadDelegate Detour = Load;
    private static LoadDelegate Original;
    private static bool Installed;
    [DllImport("libdobby.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern int DobbyHook(IntPtr address, IntPtr replacement, out IntPtr trampoline);
    private static readonly Dictionary<string, Object> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> AudioOrder = new();
    public static void PrepareLocalPlayback(AlbumRecord album)
    {
        using (var archive = CustomAlbums.Core.MdmArchive.Open(album.Path))
        {
            if (archive.Id != album.Id || archive.Charts.Count == 0)
                throw new InvalidDataException("Custom album changed after registration");
        }

        var name = album.AssetPrefix + "_music";
        if (!Cache.TryGetValue(name, out var audio) || audio == null)
            Keep(name, AudioAssets.Load(album, false));
        var cover = album.AssetPrefix + "_cover";
        if (!Cache.TryGetValue(cover, out var artwork) || artwork == null)
            Keep(cover, AudioAssets.Cover(album));
    }

    // LemonLoader NativeHookAttach is a no-op on Android; Dobby supplies the trampoline.
    public static unsafe void Attach()
    {
        if (Installed)
            return;
        var method = AccessTools.Method(typeof(ResourcesManager), nameof(ResourcesManager.LoadFromName), new[] { typeof(string) }, new[] { typeof(TextAsset) });
        if (method == null)
            throw new MissingMethodException("ResourcesManager.LoadFromName<TextAsset>(string)");
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
        if (field == null)
            throw new MissingFieldException("Generated IL2CPP MethodInfo field");
        var info = (IntPtr)field.GetValue(null);
        if (info == IntPtr.Zero)
            throw new InvalidOperationException("Missing IL2CPP MethodInfo");
        var target = *(IntPtr*)info;
        if (target == IntPtr.Zero)
            throw new InvalidOperationException("Null resource load target");
        var status = DobbyHook(target, Marshal.GetFunctionPointerForDelegate(Detour), out var trampoline);
        if (status != 0 || trampoline == IntPtr.Zero || trampoline == target)
            throw new InvalidOperationException($"Resource DobbyHook failed status={status} trampoline=0x{trampoline.ToString("x")}");
        Original = Marshal.GetDelegateForFunctionPointer<LoadDelegate>(trampoline);
        Installed = true;
    }

    private static IntPtr Load(IntPtr self, IntPtr namePointer, IntPtr method)
    {
        var original = IntPtr.Zero;
        try
        {
            var name = IL2CPP.Il2CppStringToManaged(namePointer) ?? "";
            if (Cache.TryGetValue(name, out var cached) && cached != null)
                return cached.Pointer;
            if (name == "IconCustomAlbums")
            {
                var icon = AudioAssets.SectionIcon();
                Keep(name, icon);
                return icon.Pointer;
            }

            var album = AlbumCatalog.FindAsset(name);
            if (album != null)
            {
                Object result = null;
                var suffix = name[(album.AssetPrefix.Length + 1)..];
                if (suffix == "music" || suffix == "demo")
                    result = AudioAssets.Load(album, suffix == "demo");
                else if (suffix == "cover")
                    result = AudioAssets.Cover(album);
                else if (suffix.StartsWith("map", StringComparison.Ordinal) && int.TryParse(suffix.AsSpan(3), out var difficulty))
                    return StageAdapter.Load(album, difficulty).Pointer;
                if (result != null)
                {
                    Keep(name, result);
                    return result.Pointer;
                }
            }

            if (name == AlbumCatalog.AlbumJson)
                return Publish(name, SongJson()).Pointer;
            if (name.StartsWith(AlbumCatalog.AlbumJson + "_", StringComparison.Ordinal))
                return Publish(name, new JArray(AlbumCatalog.Albums.Select(a => new JObject { ["name"] = a.Metadata.Name, ["author"] = a.Metadata.Author }))).Pointer;
            original = Original(self, namePointer, method);
            if (original == IntPtr.Zero)
                return original;
            if (name == "albums")
            {
                var json = JArray.Parse(new TextAsset(original).text);
                if (json.OfType<JObject>().Any(o => o.Value<string>("uid") == AlbumCatalog.PackageUid))
                    throw new InvalidOperationException("Custom album UID collides with existing data");
                json.Add(new JObject { ["uid"] = AlbumCatalog.PackageUid, ["title"] = "Custom Albums", ["prefabsName"] = "AlbumDisco999", ["price"] = "0", ["jsonName"] = AlbumCatalog.AlbumJson, ["needPurchase"] = false, ["free"] = true });
                return Publish(name, json).Pointer;
            }

            if (name.StartsWith("albums_", StringComparison.Ordinal))
            {
                var json = JArray.Parse(new TextAsset(original).text);
                json.Add(new JObject { ["title"] = "Custom Albums" });
                return Publish(name, json).Pointer;
            }

            if (name == "music_search_tag")
            {
                var json = JArray.Parse(new TextAsset(original).text);
                foreach (var a in AlbumCatalog.Albums)
                    json.Add(new JObject { ["uid"] = a.Uid, ["tag"] = new JArray(a.Metadata.SearchTags.Concat(new[] { "custom albums", a.Metadata.NameRomanized }).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.ToLowerInvariant())) });
                return Publish(name, json).Pointer;
            }

            return original;
        }
        catch (Exception ex)
        {
            Mod.Log.Error("CA_RESOURCE_FAILED " + ex);
            return original;
        }
    }

    private static TextAsset Publish(string name, JArray json)
    {
        var asset = new TextAsset(json.ToString(Newtonsoft.Json.Formatting.None))
        {
            name = name
        };
        Keep(name, asset);
        var manager = Singleton<ConfigManager>.instance;
        if (manager != null && !manager.m_Dictionary.ContainsKey(name))
            manager.Add(name, asset.text);
        return asset;
    }

    private static void Keep(string name, Object asset)
    {
        asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Cache[name] = asset;
        if (asset is AudioClip)
        {
            AudioOrder.Enqueue(name);
            while (AudioOrder.Count > 4)
            {
                var key = AudioOrder.Dequeue();
                if (Cache.Remove(key, out var previous))
                    Object.Destroy(previous);
            }
        }
    }

    private static JArray SongJson()
    {
        var result = new JArray();
        foreach (var album in AlbumCatalog.Albums)
        {
            var info = album.Metadata;
            var row = new JObject
            {
                ["uid"] = album.Uid,
                ["name"] = info.Name,
                ["author"] = info.Author,
                ["bpm"] = info.Bpm,
                ["music"] = album.AssetPrefix + "_music",
                ["demo"] = album.AssetPrefix + "_demo",
                ["cover"] = album.AssetPrefix + "_cover",
                ["noteJson"] = album.AssetPrefix + "_map",
                ["scene"] = info.Scene,
                ["unlockLevel"] = "0",
                ["levelDesigner"] = info.LevelDesigner
            };
            for (var d = 1; d <= 5; d++)
            {
                row["difficulty" + d] = album.Charts.ContainsKey(d) && info.Difficulties.TryGetValue(d, out var value) ? value : "0";
                row["levelDesigner" + d] = info.LevelDesigners.TryGetValue(d, out var designer) ? designer : info.LevelDesigner;
            }

            result.Add(row);
        }

        return result;
    }
}
