using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CustomAlbums.Android;
using CustomAlbums.Core;
using Newtonsoft.Json.Linq;

var root = Path.Combine(Path.GetTempPath(), "MinkyuBridge-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var failures = 0;
var results = new List<object>();
const string session = "test-launch-session-1";
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
string UriFor(string id) => "content://" + MinkyuLibraryBridge.Authority + "/chart/" + id;
byte[] Chart(string title)
{
    using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
    {
        void Add(string name, byte[] bytes)
        {
            using var entry = zip.CreateEntry(name).Open();
            entry.Write(bytes);
        }

        Add("info.json", Encoding.UTF8.GetBytes(new JObject { ["name"] = title, ["bpm"] = 120, ["difficulty2"] = "7" }.ToString()));
        Add("map2.bms", Encoding.UTF8.GetBytes("#PLAYER 3\n#GENRE scene_01\n#BPM 120\n#00014:0101\n"));
        Add("music.ogg", new byte[] { 79, 103, 103, 83, 0, 0, 0, 0 });
    }

    return memory.ToArray();
}

JObject Catalog(params string[] ids) => new()
{
    ["version"] = 1,
    ["session"] = session,
    ["albums"] = new JArray(ids.Select(id => new JObject { ["id"] = id, ["name"] = "Chart", ["uri"] = UriFor(id) }))
};
MemoryStream Json(JObject value) => new(Encoding.UTF8.GetBytes(value.ToString()));
void Assert(bool value, string detail)
{
    if (!value)
        throw new Exception(detail);
}

void Reject(Action action)
{
    try
    {
        action();
    }
    catch (Exception ex) when (ex is InvalidDataException or Newtonsoft.Json.JsonException or DecoderFallbackException)
    {
        return;
    }

    throw new Exception("Expected invalid input rejection.");
}

void Test(string name, Action action)
{
    try
    {
        action();
        results.Add(new
        {
            name,
            passed = true
        });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures++;
        results.Add(new
        {
            name,
            passed = false,
            error = ex.ToString()
        });
        Console.WriteLine("FAIL " + name + " " + ex);
    }
}

var first = Chart("First");
var second = Chart("Second");
var firstId = Hash(first);
var secondId = Hash(second);
var managed = Path.Combine(root, "managed");
try
{
    Test("import nonseekable manager charts and persist membership", () =>
    {
        var paths = MinkyuLibraryBridge.Synchronize(managed, Json(Catalog(firstId)), session, _ => new NonSeekable(first));
        Assert(paths.Count == 1 && File.ReadAllBytes(paths[0]).SequenceEqual(first), "Imported bytes differ.");
        Assert(MinkyuLibraryBridge.ReadPersistedPaths(managed).SequenceEqual(paths), "Persisted membership differs.");
    });
    Test("partial failed sync preserves previous membership", () =>
    {
        var before = File.ReadAllBytes(Path.Combine(managed, "catalog.json"));
        Reject(() => MinkyuLibraryBridge.Synchronize(managed, Json(Catalog(secondId, new string('a', 64))), session, uri => new MemoryStream(uri == UriFor(secondId) ? second : new byte[] { 1, 2, 3 })));
        Assert(File.ReadAllBytes(Path.Combine(managed, "catalog.json")).SequenceEqual(before), "Failed sync modified catalog.");
        Assert(MinkyuLibraryBridge.ReadPersistedPaths(managed).Single().EndsWith(firstId + ".mdm"), "Uncommitted cached blob became visible.");
    });
    Test("declared chart hash mismatch preserves previous membership", () =>
    {
        Reject(() => MinkyuLibraryBridge.Synchronize(managed, Json(Catalog(secondId)), session, _ => new MemoryStream(first)));
        Assert(MinkyuLibraryBridge.ReadPersistedPaths(managed).Single().EndsWith(firstId + ".mdm"), "Wrong hash replaced catalog.");
    });
    Test("session mismatch does not access chart provider", () =>
    {
        var opened = false;
        Reject(() => MinkyuLibraryBridge.Synchronize(managed, Json(Catalog(firstId)), "other-launch-session", _ =>
        {
            opened = true;
            return new MemoryStream(first);
        }));
        Assert(!opened, "Provider opened before session validation.");
    });
    Test("successful removal updates membership without deleting manual charts", () =>
    {
        var manual = new AlbumLibrary(Path.Combine(root, "manual"));
        using (var stream = new MemoryStream(first))
            manual.Import(stream);
        var paths = MinkyuLibraryBridge.Synchronize(managed, Json(Catalog()), session, _ => throw new Exception("No charts expected."));
        Assert(paths.Count == 0 && MinkyuLibraryBridge.ReadPersistedPaths(managed).Count == 0, "Empty catalog did not remove membership.");
        Assert(manual.EnumeratePaths().Count() == 1, "Manual chart was removed.");
        Assert(!Directory.EnumerateFiles(Path.Combine(managed, "Library"), "*.mdm").Any(), "Removed manager charts still occupy the game cache.");
    });
    Test("strict catalog authority paths IDs types and duplicate records", () =>
    {
        foreach (var uri in new[]
        {
            "file:///etc/passwd",
            "content://evil/chart/" + firstId,
            UriFor(firstId) + "?redirect=1",
            UriFor(firstId) + "/../x",
            "content://app.minkyu.manager.library@evil/chart/" + firstId
        }
        )
        {
            var json = Catalog(firstId);
            json["albums"][0]["uri"] = uri;
            Reject(() => MinkyuLibraryBridge.ReadCatalog(Json(json)));
        }

        Reject(() => MinkyuLibraryBridge.ReadCatalog(Json(Catalog(firstId, firstId))));
        foreach (var id in new[] { "../x", new string('z', 64), new string('a', 63) }
        )
            Reject(() => MinkyuLibraryBridge.ReadCatalog(Json(Catalog(id))));
        var wrongVersion = Catalog();
        wrongVersion["version"] = "1";
        Reject(() => MinkyuLibraryBridge.ReadCatalog(Json(wrongVersion)));
        var noSession = Catalog();
        noSession.Remove("session");
        Reject(() => MinkyuLibraryBridge.ReadCatalog(Json(noSession)));
        var noName = Catalog(firstId);
        noName["albums"][0]["name"] = 17;
        Reject(() => MinkyuLibraryBridge.ReadCatalog(Json(noName)));
    });
    Test("catalog bounds malformed JSON duplicate fields and strict UTF8", () =>
    {
        var many = Catalog(Enumerable.Range(0, 257).Select(i => i.ToString("x64")).ToArray());
        Reject(() => MinkyuLibraryBridge.ReadCatalog(Json(many)));
        Reject(() => MinkyuLibraryBridge.ReadCatalog(new MemoryStream(new byte[MinkyuLibraryBridge.MaxCatalogBytes + 1])));
        Reject(() => MinkyuLibraryBridge.ReadCatalog(new MemoryStream(new byte[] { 0xff, 0xfe })));
        Reject(() => MinkyuLibraryBridge.ReadCatalog(new MemoryStream(Encoding.UTF8.GetBytes("{\"version\":1,\"version\":1,\"session\":\"s\",\"albums\":[]}"))));
        Reject(() => MinkyuLibraryBridge.ReadCatalog(new MemoryStream(Encoding.UTF8.GetBytes(Catalog().ToString() + " {}"))));
    });
    Test("failed first sync never exposes uncommitted chart objects", () =>
    {
        var fresh = Path.Combine(root, "fresh");
        Reject(() => MinkyuLibraryBridge.Synchronize(fresh, Json(Catalog(firstId, secondId)), session, uri => new MemoryStream(uri == UriFor(firstId) ? first : new byte[] { 0 })));
        Assert(MinkyuLibraryBridge.ReadPersistedPaths(fresh).Count == 0, "Uncommitted first library became visible.");
    });
    Test("manual and persisted manager catalogs deduplicate by content hash", () =>
    {
        var app = Path.Combine(root, "app");
        UnityEngine.Application.persistentDataPath = app;
        var caRoot = Path.Combine(app, "CustomAlbums");
        var manual = new AlbumLibrary(Path.Combine(caRoot, "Library"));
        using (var stream = new MemoryStream(first))
            manual.Import(stream);
        MinkyuLibraryBridge.Synchronize(Path.Combine(caRoot, "MinkyuLibrary"), Json(Catalog(firstId, secondId)), session, uri => new MemoryStream(uri == UriFor(firstId) ? first : second));
        AlbumCatalog.Load();
        Assert(AlbumCatalog.Albums.Count == 2 && AlbumCatalog.Albums.Select(a => a.Id).Distinct().Count() == 2, "Hash deduplication failed.");
        Assert(AlbumCatalog.Albums.Single(a => a.Id == firstId).Path.StartsWith(manual.RootDirectory), "Manual duplicate was not preferred.");
        AlbumCatalog.Load();
        Assert(AlbumCatalog.Albums.Count == 2, "Repeated catalog initialization duplicated records.");
    });
    Test("expansion atomic replacement and verified reuse", () =>
    {
        var directory = Path.Combine(root, "obb");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "main.626.com.prpr.musedash.obb");
        File.WriteAllText(path, "previous expansion");
        var replacement = Encoding.UTF8.GetBytes("replacement expansion bytes");
        Reject(() => MinkyuLibraryBridge.ImportExpansion(directory, Hash(replacement), () => new MemoryStream(new byte[] { 1 })));
        Assert(File.ReadAllText(path) == "previous expansion", "Hash failure destroyed previous OBB.");
        Reject(() => MinkyuLibraryBridge.ImportExpansion(directory, Hash(Array.Empty<byte>()), () => new MemoryStream()));
        Assert(File.ReadAllText(path) == "previous expansion", "Empty import destroyed previous OBB.");
        MinkyuLibraryBridge.ImportExpansion(directory, Hash(replacement), () => new NonSeekable(replacement));
        Assert(File.ReadAllBytes(path).SequenceEqual(replacement), "Replacement expansion differs.");
        MinkyuLibraryBridge.ImportExpansion(directory, Hash(replacement), () => throw new Exception("Verified OBB should be reused."));
        Assert(!Directory.EnumerateFiles(directory, "*.pending").Any(), "Pending files leaked.");
    });
    Test("expansion rejected invalid hash and relative directory before opening provider", () =>
    {
        Reject(() => MinkyuLibraryBridge.ImportExpansion(Path.Combine(root, "obb2"), "../invalid", () => throw new Exception("Provider should not open.")));
        Reject(() => MinkyuLibraryBridge.ImportExpansion("relative", firstId, () => throw new Exception("Provider should not open.")));
    });
    Test("launch verification reuses only a matching file and hash", () =>
    {
        var path = Path.Combine(root, "verified.obb");
        var bytes = Encoding.UTF8.GetBytes("valid data");
        File.WriteAllBytes(path, bytes);
        AppContext.SetData(Minkyu.Shared.LaunchFileVerification.ReceiptKey, null);
        Assert(Minkyu.Shared.LaunchFileVerification.Verify(path, Hash(bytes), 100, out var cached) && !cached, "First check skipped hashing.");
        Assert(Minkyu.Shared.LaunchFileVerification.Verify(path, Hash(bytes), 100, out cached) && cached, "Second check did not reuse receipt.");
        Assert(!Minkyu.Shared.LaunchFileVerification.Verify(path, secondId, 100, out _), "Different expected hash accepted.");
        var other = Path.Combine(root, "other.obb");
        File.WriteAllBytes(other, bytes);
        Assert(Minkyu.Shared.LaunchFileVerification.Verify(other, Hash(bytes), 100, out cached) && !cached, "Different path reused receipt.");
    });
    Test("changed or missing expansion invalidates launch receipt", () =>
    {
        var path = Path.Combine(root, "changed.obb");
        var bytes = Encoding.UTF8.GetBytes("valid data");
        File.WriteAllBytes(path, bytes);
        Assert(Minkyu.Shared.LaunchFileVerification.Verify(path, Hash(bytes), 100, out _), "Initial file rejected.");
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("wrong data"));
        File.SetLastWriteTimeUtc(path, time.AddSeconds(2));
        Assert(!Minkyu.Shared.LaunchFileVerification.Verify(path, Hash(bytes), 100, out _), "Same-sized changed file trusted.");
        File.Delete(path);
        Assert(!Minkyu.Shared.LaunchFileVerification.Verify(path, Hash(bytes), 100, out _), "Missing file trusted.");
    });
    Test("new launch or smaller file limit cannot reuse verification", () =>
    {
        var path = Path.Combine(root, "new-launch.obb");
        var bytes = Encoding.UTF8.GetBytes("valid data");
        File.WriteAllBytes(path, bytes);
        Assert(Minkyu.Shared.LaunchFileVerification.Verify(path, Hash(bytes), 100, out _), "Initial file rejected.");
        Assert(!Minkyu.Shared.LaunchFileVerification.Verify(path, Hash(bytes), 1, out _), "Byte limit bypassed by receipt.");
        AppContext.SetData(Minkyu.Shared.LaunchFileVerification.ReceiptKey, null);
        Assert(Minkyu.Shared.LaunchFileVerification.Verify(path, Hash(bytes), 100, out var cached) && !cached, "New process must verify content.");
    });
}
finally
{
    var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "bridge-tests.json");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new
    {
        passed = results.Count - failures,
        failed = failures,
        androidJniTested = false,
        tests = results
    }, Newtonsoft.Json.Formatting.Indented));
    Console.WriteLine($"RESULT passed={results.Count - failures} failed={failures} evidence={output}");
    Directory.Delete(root, true);
}

return failures == 0 ? 0 : 1;
sealed class NonSeekable(byte[] bytes) : Stream
{
    private readonly MemoryStream inner = new(bytes);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException(); set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
