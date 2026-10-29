using CustomAlbums.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomAlbums.Android;

internal sealed class AlbumRecord
{
    public string Path { get; init; }
    public string Id { get; init; }
    public int Index { get; init; }
    public string AssetPrefix => "album_android_" + Id;
    public string Uid => "999-" + Index;
    public AlbumMetadata Metadata { get; init; }
    public IReadOnlyDictionary<int, BmsChart> Charts { get; init; }
}

internal static class AlbumCatalog
{
    public const string AlbumJson = "ALBUM1000";
    public const string PackageUid = "music_package_999";
    public static readonly List<AlbumRecord> Albums = new();
    public static string Root { get; private set; }

    public static void Load()
    {
        Root = System.IO.Path.Combine(Application.persistentDataPath, "CustomAlbums");
        var inbox = System.IO.Path.Combine(Root, "Inbox");
        Directory.CreateDirectory(inbox);
        var library = new AlbumLibrary(System.IO.Path.Combine(Root, "Library"));
        var managedPaths = MinkyuLibraryBridge.Load(Root);
        foreach (var file in Directory.EnumerateFiles(inbox, "*.mdm"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                library.Import(stream);
            }
            catch (Exception ex)
            {
                Mod.Log.Error("Import rejected " + System.IO.Path.GetFileName(file) + ": " + ex.Message);
            }
        }

        var indexPath = System.IO.Path.Combine(Root, "uid-index.json");
        var index = File.Exists(indexPath) ? JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(indexPath)) ?? new() : new Dictionary<string, int>();
        var next = index.Count == 0 ? 0 : checked(index.Values.Max() + 1);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        Albums.Clear();
        foreach (var file in library.EnumeratePaths().Concat(managedPaths).OrderBy(x => x, StringComparer.Ordinal))
        {
            try
            {
                using var archive = MdmArchive.Open(file);
                if (!loaded.Add(archive.Id))
                    continue;
                if (!index.TryGetValue(archive.Id, out var id))
                    index[archive.Id] = id = next++;
                Albums.Add(new AlbumRecord { Path = file, Id = archive.Id, Index = id, Metadata = archive.Metadata, Charts = archive.Charts });
            }
            catch (Exception ex)
            {
                Mod.Log.Error("Library entry rejected: " + ex.Message);
            }
        }

        var temporary = indexPath + ".pending";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(index, Formatting.Indented));
        File.Move(temporary, indexPath, true);
        Mod.Log.Msg("CA_LIBRARY_READY albums=" + Albums.Count + " inbox=" + inbox);
    }

    public static AlbumRecord FindAsset(string name) => Albums.FirstOrDefault(a => name.StartsWith(a.AssetPrefix + "_", StringComparison.Ordinal));
}
