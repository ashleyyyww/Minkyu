using Android.Content;
using CustomAlbums.Core;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using Path = System.IO.Path;

namespace Minkyu.Manager;

public sealed class LibraryItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public long Bytes { get; set; }
}

public sealed class LibraryStore
{
    public const string GamePackage = "com.prpr.musedash";
    public const string Authority = "app.minkyu.manager.library";
    private static readonly object Gate = new();
    private readonly string root;
    private string Index => Path.Combine(root, "library.json");
    public string ObjectDirectory => Path.Combine(root, "objects");
    public string ExpansionPath => Path.Combine(root, "main.626.com.prpr.musedash.obb");
    public string ExpansionHashPath => Path.Combine(root, "expansion.sha256");
    public string CatalogPath => Path.Combine(root, "launch-catalog.json");
    public string PreparedApk => Path.Combine(Path.GetDirectoryName(root)!, "prepared", "MuseDash-Minkyu.apk");

    public LibraryStore(Context context)
    {
        root = Path.Combine(context.FilesDir!.AbsolutePath, "library");
        Directory.CreateDirectory(ObjectDirectory);
    }

    public List<LibraryItem> Read()
    {
        lock (Gate)
            return File.Exists(Index) ? JsonConvert.DeserializeObject<List<LibraryItem>>(File.ReadAllText(Index)) ?? new() : new();
    }

    public void Save(List<LibraryItem> items)
    {
        lock (Gate)
        {
            var pending = Index + ".pending";
            using (var output = File.Create(pending))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(items, Formatting.Indented));
                output.Write(bytes);
                output.Flush(true);
            }

            File.Move(pending, Index, true);
        }
    }

    public LibraryItem Import(Stream stream)
    {
        var library = new AlbumLibrary(ObjectDirectory);
        var path = library.Import(stream);
        using var archive = MdmArchive.Open(path);
        lock (Gate)
        {
            var items = Read();
            var existing = items.FirstOrDefault(i => i.Id == archive.Id);
            if (existing != null)
                return existing;
            var item = new LibraryItem
            {
                Id = archive.Id,
                Name = archive.Metadata.Name,
                Author = archive.Metadata.Author,
                Difficulty = string.Join(" / ", archive.Metadata.Difficulties.Where(d => archive.Charts.ContainsKey(d.Key)).Select(d => d.Value)),
                Bytes = new FileInfo(path).Length
            };
            items.Add(item);
            Save(items);
            return item;
        }
    }

    public (LibraryItem Item, bool Moved) ImportAndMove(Func<Stream> openSource, Func<bool> deleteSource)
    {
        LibraryItem item;
        using (var source = openSource())
            item = Import(source);
        try
        {
            // Delete the provider source only after saving our validated copy and rechecking it.
            using (var check = openSource())
                if (!Convert.ToHexString(SHA256.HashData(check)).Equals(item.Id, StringComparison.OrdinalIgnoreCase))
                    return (item, false);
            return (item, deleteSource());
        }
        catch (Exception ex)when (ex is not OutOfMemoryException)
        {
            return (item, false);
        }
    }

    public void Remove(string id)
    {
        lock (Gate)
        {
            var items = Read();
            var file = ChartPath(id);
            if (File.Exists(file))
                File.Delete(file);
            items.RemoveAll(i => i.Id == id);
            Save(items);
        }
    }

    public string ChartPath(string id)
    {
        if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c)) || id != id.ToLowerInvariant())
            throw new InvalidDataException("Invalid chart identity");
        return Path.Combine(ObjectDirectory, id + ".mdm");
    }

    public byte[]? Cover(LibraryItem item)
    {
        using var archive = MdmArchive.Open(ChartPath(item.Id));
        return archive.CoverEntry == null ? null : archive.ReadAssetBytes(archive.CoverEntry, 32 * 1024 * 1024);
    }

    public string CreateCatalog()
    {
        var session = Guid.NewGuid().ToString("N");
        var items = Read();
        if (items.Count > 256)
            throw new InvalidDataException("Your library can have up to 256 songs.");
        var catalog = new
        {
            version = 1,
            session,
            albums = items.Select(i => new { id = i.Id, name = i.Name, uri = $"content://{Authority}/chart/{i.Id}" })
        };
        var pending = CatalogPath + ".pending";
        File.WriteAllText(pending, JsonConvert.SerializeObject(catalog));
        File.Move(pending, CatalogPath, true);
        return session;
    }

    public void ImportExpansion(Stream source, Action<string> progress)
    {
        var pending = ExpansionPath + ".pending";
        try
        {
            using (var output = File.Create(pending))
            {
                var buffer = new byte[1024 * 1024];
                long total = 0;
                int count;
                long report = 0;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += count;
                    if (total > 4L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("Game data exceeds 4 GB.");
                    output.Write(buffer, 0, count);
                    if (total - report > 64 * 1024 * 1024)
                    {
                        report = total;
                        progress($"Copying game data · {total / 1024 / 1024:N0} MB");
                    }
                }

                if (total == 0)
                    throw new InvalidDataException("Game data is empty.");
                output.Flush(true);
            }

            using (var archive = ZipFile.OpenRead(pending))
                if (!archive.Entries.Any(e => e.FullName.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Select the Muse Dash main.626 OBB archive.");
            using (var input = File.OpenRead(pending))
                File.WriteAllText(ExpansionHashPath + ".pending", Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant());
            File.Move(pending, ExpansionPath, true);
            File.Move(ExpansionHashPath + ".pending", ExpansionHashPath, true);
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
    }
}
