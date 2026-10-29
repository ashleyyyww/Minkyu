using CustomAlbums.Core;
using Newtonsoft.Json;

if (args.Length < 2 || (args[0] != "inspect" && args[0] != "import" && args[0] != "scan"))
{
    Console.Error.WriteLine("Usage: CustomAlbums.Cli inspect <song.mdm> | scan <directory> | import <song.mdm> <library-directory>");
    return 2;
}

try
{
    if (args[0] == "import")
    {
        if (args.Length != 3)
            throw new ArgumentException("import requires source and destination directory.");
        using var source = File.OpenRead(args[1]);
        Console.WriteLine(new AlbumLibrary(args[2]).Import(source));
        return 0;
    }

    var paths = args[0] == "scan" ? Directory.EnumerateFiles(args[1], "*.mdm").Order(StringComparer.Ordinal).ToArray() : new[] { args[1] };
    var failed = false;
    var reports = new List<object>();
    foreach (var path in paths)
    {
        try
        {
            using var album = MdmArchive.Open(path);
            reports.Add(new { path, success = true, album.Id, album.Metadata, album.MusicEntry, album.DemoEntry, album.CoverEntry, charts = album.Charts.Values.Select(c => new { c.Difficulty, c.EntryName, c.BaseBpm, c.Md5, eventCount = c.Events.Count, lastEventSeconds = c.Events.Count == 0 ? 0 : c.Events[^1].Seconds, c.Headers, c.Warnings }), album.Assets, album.Warnings });
        }
        catch (Exception ex)
        {
            failed = true;
            reports.Add(new { path, success = false, error = ex.Message });
        }
    }

    Console.WriteLine(JsonConvert.SerializeObject(reports, Formatting.Indented));
    return failed ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
