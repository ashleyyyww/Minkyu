using System.IO.Compression;
using System.Text;
using Minkyu.Manager;
using Newtonsoft.Json.Linq;

var root = Path.Combine(Path.GetTempPath(), "MinkyuMoveTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var results = new List<object>();
var failures = 0;
void Assert(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
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

LibraryStore Store(string name) => new(new Android.Content.Context(Path.Combine(root, name)));
byte[] Chart(string title)
{
    using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
    {
        void Add(string name, string text)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(text);
        }

        Add("info.json", new JObject { ["name"] = title, ["bpm"] = 120, ["difficulty2"] = "7" }.ToString());
        Add("map2.bms", "#PLAYER 3\n#BPM 120\n#00014:0101\n");
        Add("music.ogg", "OggSplaceholder");
    }

    return memory.ToArray();
}

try
{
    var bytes = Chart("Move test");
    Test("move preserves validated app copy before removing source", () =>
    {
        var store = Store("move");
        var source = Path.Combine(root, "download.mdm");
        File.WriteAllBytes(source, bytes);
        var result = store.ImportAndMove(() => File.OpenRead(source), () =>
        {
            var item = store.Read().Single();
            Assert(File.ReadAllBytes(store.ChartPath(item.Id)).SequenceEqual(bytes), "Source deleted before app copy was saved.");
            File.Delete(source);
            return true;
        });
        Assert(result.Moved && !File.Exists(source), "Source remained after move.");
        Assert(File.ReadAllBytes(store.ChartPath(result.Item.Id)).SequenceEqual(bytes), "Stored song changed.");
    });
    Test("invalid archive never deletes source", () =>
    {
        var store = Store("invalid");
        var deleted = false;
        try
        {
            store.ImportAndMove(() => new MemoryStream(new byte[] { 1, 2, 3 }), () =>
            {
                deleted = true;
                return true;
            });
            throw new Exception("Invalid file accepted.");
        }
        catch (InvalidDataException)
        {
        }

        Assert(!deleted && store.Read().Count == 0, "Invalid source deleted or registered.");
    });
    Test("changed source is retained after successful import", () =>
    {
        var store = Store("changed");
        var opened = 0;
        var deleted = false;
        var result = store.ImportAndMove(() => new MemoryStream(++opened == 1 ? bytes : Chart("Changed")), () =>
        {
            deleted = true;
            return true;
        });
        Assert(!result.Moved && !deleted && store.Read().Count == 1, "Changed source was deleted.");
    });
    Test("provider refusal retains app copy and reports source kept", () =>
    {
        var store = Store("refused");
        var result = store.ImportAndMove(() => new MemoryStream(bytes), () => false);
        Assert(!result.Moved && File.Exists(store.ChartPath(result.Item.Id)), "Refused deletion lost imported song.");
        result = store.ImportAndMove(() => new MemoryStream(bytes), () => throw new UnauthorizedAccessException());
        Assert(!result.Moved && store.Read().Count == 1, "Access denial lost or duplicated song.");
    });
    Test("duplicate reimport moves source without duplicating app song", () =>
    {
        var store = Store("duplicate");
        using (var stream = new MemoryStream(bytes))
            store.Import(stream);
        var removed = false;
        var result = store.ImportAndMove(() => new MemoryStream(bytes), () =>
        {
            removed = true;
            return true;
        });
        Assert(result.Moved && removed && store.Read().Count == 1, "Duplicate move did not deduplicate.");
        Assert(Directory.EnumerateFiles(store.ObjectDirectory, "*.mdm").Count() == 1, "Duplicate file retained.");
    });
    Test("delete removes stored mdm and catalog entry only", () =>
    {
        var store = Store("delete");
        using var first = new MemoryStream(bytes);
        var item = store.Import(first);
        using var otherStream = new MemoryStream(Chart("Keep me"));
        var other = store.Import(otherStream);
        store.Remove(item.Id);
        Assert(!File.Exists(store.ChartPath(item.Id)), "Deleted song file remains.");
        Assert(store.Read().Single().Id == other.Id && File.Exists(store.ChartPath(other.Id)), "Other song was removed.");
        var catalog = store.CreateCatalog();
        Assert(JObject.Parse(File.ReadAllText(store.CatalogPath))["albums"]!.Count() == 1, "Deleted song in launch catalog.");
    });
    Test("old disabled songs migrate to ordinary library songs", () =>
    {
        var store = Store("legacy");
        using var stream = new MemoryStream(bytes);
        var item = store.Import(stream);
        var index = Path.Combine(root, "legacy", "library", "library.json");
        var json = JArray.Parse(File.ReadAllText(index));
        json[0]!["Enabled"] = false;
        json[0]!["Collection"] = "Old group";
        File.WriteAllText(index, json.ToString());
        store.CreateCatalog();
        Assert(JObject.Parse(File.ReadAllText(store.CatalogPath))["albums"]!.Single()["id"]!.Value<string>() == item.Id, "Old disabled song stayed hidden.");
        store.Save(store.Read());
        var migrated = File.ReadAllText(index);
        Assert(!migrated.Contains("Enabled") && !migrated.Contains("Collection"), "Removed fields still saved.");
    });
}
finally
{
    var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "library-tests.json");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new
    {
        passed = results.Count - failures,
        failed = failures,
        androidDocumentProviderTested = false,
        tests = results
    }, Newtonsoft.Json.Formatting.Indented));
    Console.WriteLine($"RESULT passed={results.Count - failures} failed={failures}");
    Directory.Delete(root, true);
}
return failures == 0 ? 0 : 1;
