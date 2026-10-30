using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CustomAlbums.Core;
using Newtonsoft.Json;

var results = new List<object>();
var failures = 0;
var tempRoot = Path.Combine(Path.GetTempPath(), "CustomAlbums-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
void Test(string name, Action run)
{
    var timer = Stopwatch.StartNew();
    try
    {
        run();
        results.Add(new
        {
            name,
            passed = true,
            milliseconds = timer.ElapsedMilliseconds
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
            error = ex.ToString(),
            milliseconds = timer.ElapsedMilliseconds
        });
        Console.WriteLine("FAIL " + name + ": " + ex.Message);
    }
}

void Assert(bool value, string message)
{
    if (!value)
        throw new Exception(message);
}

void Reject(Action action)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        return;
    }

    throw new Exception("Expected InvalidDataException");
}

string Zip(string name, params (string name, byte[] bytes)[] assets)
{
    var path = Path.Combine(tempRoot, name + ".mdm");
    using var file = File.Create(path);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    foreach (var asset in assets)
    {
        using var stream = archive.CreateEntry(asset.name).Open();
        stream.Write(asset.bytes);
    }

    return path;
}

byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
var validInfo = Utf8("{\"name\":\"Test\",\"bpm\":120,\"difficulty2\":\"7\",\"searchTags\":[\"test\"]}");
var validBms = Utf8("#PLAYER 3\n#GENRE scene_01\n#BPM 120\n#00014:0101\n#00154:0F0F\n");
var validMusic = new byte[] { 79, 103, 103, 83, 0, 0, 0, 0 };
(string, byte[])[] Valid() => new[] { ("info.json", validInfo), ("map2.bms", validBms), ("music.ogg", validMusic) };
try
{
    Test("custom records keep separate maxima across clears and restarts", () =>
    {
        var path = Path.Combine(tempRoot, "chart-records.json");
        var hash = new string('a', 32);
        var records = new ChartRecords(path);
        records.ImportSummary(hash, 162585, 96.63f);
        records.RecordClear(hash, 115770, 82.54f, 93, 3, false);
        records.RecordClear(hash, 160000, 97, 220, 5, true);
        var result = new ChartRecords(path).Get(hash)!;
        if (result.Score != 162585 || result.Accuracy != 97 || result.Combo != 220 || result.Evaluate != 5 || result.Clears != 2 || !result.FullCombo)
            throw new Exception("A lower score replaced the best, or clear details were lost");
        if (new ChartRecords(path).Get(new string('b', 32)) != null)
            throw new Exception("Different chart inherited a record");
        result.Score = 0;
        if (records.Get(hash)!.Score != 162585) throw new Exception("A caller changed stored data");
    });
    Test("custom records reject malformed data without replacing the save", () =>
    {
        var path = Path.Combine(tempRoot, "record-rejection.json");
        var hash = new string('c', 32);
        var records = new ChartRecords(path);
        records.ImportSummary(hash, 20, 90);
        var before = File.ReadAllBytes(path);
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, -1, 101 })
        {
            try { records.RecordClear(hash, 100, value, 10, 5, true); throw new Exception("Invalid accuracy accepted"); }
            catch (InvalidDataException) { }
        }
        if (!before.SequenceEqual(File.ReadAllBytes(path))) throw new Exception("Rejected data changed save");
    });
    Test("cinema manifest follows the PC format and rejects external video references", () =>
    {
        var extra = new[] { ("cinema.json", Utf8("{\"file_name\":\"video.mp4\",\"opacity\":0.3,\"difficulties\":[1,2]}")), ("video.mp4", new byte[] { 1, 2, 3 }) };
        using (var archive = MdmArchive.Open(Zip("cinema", Valid().Concat(extra).ToArray())))
        {
            var cinema = CinemaSettings.Read(archive)!;
            Assert(cinema.FileName == "video.mp4" && Math.Abs(cinema.Opacity - .3f) < .001 && cinema.Difficulties.SequenceEqual(new[] { 1, 2 }), "PC cinema fields changed");
        }
        foreach (var name in new[] { "https://example.invalid/video.mp4", "../outside.mp4", "/outside.mp4" })
        {
            using var archive = MdmArchive.Open(Zip(Guid.NewGuid().ToString("N"), Valid().Concat(new[] { ("cinema.json", Utf8("{\"file_name\":\"" + name + "\",\"opacity\":1}")) }).ToArray()));
            Reject(() => CinemaSettings.Read(archive));
        }
    });
    Test("dialogue versions and language tracks are preserved with bounded input", () =>
    {
        using var archive = MdmArchive.Open(Zip("talk", Valid().Concat(new[] { ("map1.talk", Utf8("{\"version\":2,\"English\":[{\"time\":\"2.09\",\"text\":\"Hello\",\"dialogState\":1}]}")) }).ToArray()));
        var dialogue = ChartMedia.ReadDialogue(archive, 1)!;
        Assert(dialogue.Value<int>("version") == 2 && dialogue["English"]![0]!.Value<string>("text") == "Hello", "Dialogue text or version changed");
        Assert(ChartMedia.ReadDialogue(archive, 2) == null, "Missing difficulty inherited dialogue");
    });
    Test("out-of-order BPM and measure multipliers", () =>
    {
        var bms = BmsParser.Parse(Utf8("#BPM 120\n#GENRE scene_01\n#00214:01\n#00014:0001\n#00102:0.5\n#00114:0001\n#00008:0001\n#BPM01 240\n"));
        var notes = bms.Events.Where(e => e.Channel == "14").ToArray();
        Assert(notes[0].Seconds == 1m && notes[1].Seconds == 1.75m && notes[2].Seconds == 2m, "Expected 1, 1.75, 2 seconds");
    });
    Test("direct hexadecimal BPM and event ordering", () =>
    {
        var bms = BmsParser.Parse(Utf8("#BPM 120\n#GENRE scene_01\n#00114:01\n#00014:01\n#00015:0P\n#00003:F0\n"));
        Assert(bms.Events[0].Channel == "03" && bms.Events[1].Channel == "15", "Timing and lane events must precede notes.");
        Assert(bms.Events.Last().Seconds == 1m, "F0 is 240 BPM.");
    });
    Test("reject missing and invalid BPM references", () =>
    {
        Reject(() => BmsParser.Parse(Utf8("#BPM 120\n#00008:ZZ\n")));
        Reject(() => BmsParser.Parse(Utf8("#BPM 0\n#00014:01\n")));
        Reject(() => BmsParser.Parse(Utf8("#BPM 120\n#00003:ZZ\n")));
    });
    Test("reject malformed pairs and STOP/random timing", () =>
    {
        Reject(() => BmsParser.Parse(Utf8("#BPM 120\n#00014:001\n")));
        Reject(() => BmsParser.Parse(Utf8("#BPM 120\n#00009:01\n")));
        Reject(() => BmsParser.Parse(Utf8("#BPM 120\n#RANDOM 2\n")));
        Reject(() => BmsParser.Parse(Utf8("#BPM 120\n#00014:0101\n"), maxEvents: 1));
    });
    Test("reject path traversal, rooted paths, symlinks and case collisions", () =>
    {
        foreach (var unsafeName in new[] { "../outside", "/rooted", "a\\b", "C:/absolute", "a/./b", "a//b", "a/../b", "a./b" }
        )
            Reject(() =>
            {
                using var ignored = MdmArchive.Open(Zip(Guid.NewGuid().ToString("N"), Valid().Concat(new[] { (unsafeName, new byte[] { 1 }) }).ToArray()));
            });
        Reject(() =>
        {
            using var ignored = MdmArchive.Open(Zip("duplicate", Valid().Concat(new[] { ("INFO.JSON", validInfo) }).ToArray()));
        });
        var path = Zip("symlink", Valid());
        using (var file = File.Open(path, FileMode.Open))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Update))
            archive.GetEntry("info.json")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        Reject(() =>
        {
            using var ignored = MdmArchive.Open(path);
        });
    });
    Test("enforce compressed, uncompressed, metadata and count limits", () =>
    {
        var path = Zip("limits", Valid());
        foreach (var options in new[]
        {
            new MdmReadOptions
            {
                MaxArchiveBytes = 5
            },
            new MdmReadOptions
            {
                MaxTotalUncompressedBytes = 5
            },
            new MdmReadOptions
            {
                MaxEntryBytes = 5
            },
            new MdmReadOptions
            {
                MaxMetadataBytes = 5
            },
            new MdmReadOptions
            {
                MaxEntries = 1
            },
            new MdmReadOptions
            {
                MaxChartBytes = 5
            }
        }
        )
            Reject(() =>
            {
                using var ignored = MdmArchive.Open(path, options);
            });
        var bomb = Zip("bomb", Valid().Concat(new[] { ("bomb.bin", new byte[100000]) }).ToArray());
        Reject(() =>
        {
            using var ignored = MdmArchive.Open(bomb, new MdmReadOptions { MaxCompressionRatio = 20 });
        });
    });
    Test("metadata numbers, comments and trailing commas", () =>
    {
        var info = Utf8("{ /* comment */ \"name\":\"Numeric\", \"bpm\":179.5, \"difficulty2\":9, \"hideBmsDifficulty\":3, }");
        using var archive = MdmArchive.Open(Zip("numeric", ("info.json", info), ("map2.bms", validBms), ("music.ogg", validMusic)));
        Assert(archive.Metadata.Bpm == "179.5" && archive.Metadata.Difficulties[2] == "9" && archive.Metadata.HiddenDifficulty == "3", "Numeric metadata not preserved.");
    });
    Test("reject corrupted CRC and encrypted central-directory flags", () =>
    {
        foreach (var delta in new[] { 8, 16 }
        )
        {
            var path = Zip("corrupt-" + delta, Valid());
            var bytes = File.ReadAllBytes(path);
            var central = -1;
            for (var i = 0; i + 46 < bytes.Length; i++)
                if (BitConverter.ToUInt32(bytes, i) == 0x02014b50)
                {
                    central = i;
                    break;
                }

            Assert(central >= 0, "Central directory not found in fixture.");
            bytes[central + delta] ^= 1;
            File.WriteAllBytes(path, bytes);
            Reject(() =>
            {
                using var ignored = MdmArchive.Open(path);
            });
        }
    });
    Test("reject ambiguous metadata and incorrect audio", () =>
    {
        foreach (var info in new[]
        {
            "{\"name\":\"A\",\"Name\":\"B\",\"difficulty2\":2}",
            "{\"name\":\"A\",\"name\":\"B\"}",
            "{\"name\":\"A\",\"difficulty2\":{}}",
            "{\"name\":\"A\",\"difficulty2\":2}{}"
        }
        )
            Reject(() =>
            {
                using var ignored = MdmArchive.Open(Zip(Guid.NewGuid().ToString("N"), ("info.json", Utf8(info)), ("map2.bms", validBms), ("music.ogg", validMusic)));
            });
        Reject(() =>
        {
            using var ignored = MdmArchive.Open(Zip("audio", ("info.json", validInfo), ("map2.bms", validBms), ("music.ogg", new byte[] { 1, 2, 3, 4 })));
        });
    });
    Test("atomic persistent import, duplicate handling and interrupted input", () =>
    {
        var path = Zip("valid", Valid());
        var library = new AlbumLibrary(Path.Combine(tempRoot, "library"));
        string imported;
        using (var source = new NonSeekableStream(File.OpenRead(path)))
            imported = library.Import(source);
        using (var source = File.OpenRead(path))
            Assert(library.Import(source) == imported, "Duplicate import changed identity.");
        Assert(library.EnumeratePaths().Count() == 1, "Duplicate created another file.");
        using (var a = MdmArchive.Open(imported))
            Assert(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(imported)) && a.Id.Length == 64, "Import changed bytes.");
        var badSource = new MemoryStream(new byte[] { 1, 2, 3 });
        Reject(() => library.Import(badSource));
        Assert(!Directory.EnumerateFiles(library.RootDirectory, "*.pending").Any(), "Failed import left pending file.");
        var reopened = new AlbumLibrary(library.RootDirectory);
        Assert(reopened.EnumeratePaths().Count() == 1, "Import not persistent.");
        using var mdm = MdmArchive.Open(imported);
        using var asset = mdm.OpenAsset("map2.bms");
        using var output = new MemoryStream();
        asset.CopyTo(output);
        Assert(output.ToArray().SequenceEqual(validBms), "Raw BMS changed.");
    });
    Test("asset extraction preserves caller files and removes failed partial output", () =>
    {
        var sourcePath = Zip("extract", Valid());
        var targetPath = Path.Combine(tempRoot, "extracted.ogg");
        using (var archive = MdmArchive.Open(sourcePath))
        {
            archive.ExtractAsset("music.ogg", targetPath);
            Assert(File.ReadAllBytes(targetPath).SequenceEqual(validMusic), "Extracted audio changed.");
            try
            {
                archive.ExtractAsset("music.ogg", targetPath);
                throw new Exception("Existing output should not be overwritten.");
            }
            catch (IOException)
            {
            }

            Assert(File.ReadAllBytes(targetPath).SequenceEqual(validMusic), "Caller-owned file was changed.");
        }

        var bytes = File.ReadAllBytes(sourcePath);
        var signatureCount = 0;
        for (var i = 0; i + 46 < bytes.Length; i++)
            if (BitConverter.ToUInt32(bytes, i) == 0x02014b50 && ++signatureCount == 3)
            {
                bytes[i + 16] ^= 1;
                break;
            }

        File.WriteAllBytes(sourcePath, bytes);
        var failedPath = Path.Combine(tempRoot, "failed-extract.ogg");
        using var invalidAsset = MdmArchive.Open(sourcePath);
        Reject(() => invalidAsset.ExtractAsset("music.ogg", failedPath));
        Assert(!File.Exists(failedPath), "Partial extraction was left on disk.");
    });
    var sourceDirectory = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "charts");
    Directory.CreateDirectory(sourceDirectory);
    foreach (var path in Directory.EnumerateFiles(sourceDirectory, "*.mdm").Order(StringComparer.Ordinal))
        Test("real chart: " + Path.GetFileName(path), () =>
        {
            using var archive = MdmArchive.Open(path);
            Assert(archive.Charts.Count > 0 && archive.Metadata.Name.Length > 0, "No charts or metadata.");
            CinemaSettings.Read(archive);
            foreach (var chart in archive.Charts.Values)
            {
                ChartMedia.ReadDialogue(archive, chart.Difficulty);
                Assert(chart.Events.Count > 0, "Empty chart.");
                for (var i = 1; i < chart.Events.Count; i++)
                    Assert(chart.Events[i].Seconds >= chart.Events[i - 1].Seconds, "Nonmonotonic event timing.");
                Assert(archive.ReadAssetBytes(chart.EntryName, 16 * 1024 * 1024).SequenceEqual(chart.RawBytes), "Chart bytes modified.");
            }

            using var rawZip = ZipFile.OpenRead(path);
            foreach (var name in new[] { archive.MusicEntry, archive.DemoEntry, archive.CoverEntry }.Where(n => n != null))
            {
                using var actual = archive.OpenAsset(name!);
                using var expected = rawZip.GetEntry(name!)!.Open();
                Assert(SHA256.HashData(actual).SequenceEqual(SHA256.HashData(expected)), "Asset bytes modified.");
            }
        });
}
finally
{
    var reportPath = args.Length > 1 ? args[1] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "parser-tests.json"));
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    File.WriteAllText(reportPath, JsonConvert.SerializeObject(new
    {
        timestampUtc = DateTime.UtcNow.ToString("O"),
        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        total = results.Count,
        passed = results.Count - failures,
        failed = failures,
        scope = "Host parser, ZIP safety, lossless asset reads and import persistence. No Unity playback or Android execution.",
        results
    }, Formatting.Indented));
    Console.WriteLine("Report: " + reportPath);
    Directory.Delete(tempRoot, true);
}

return failures == 0 ? 0 : 1;
sealed class NonSeekableStream(Stream inner) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException(); set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override void Flush()
    {
    }

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
