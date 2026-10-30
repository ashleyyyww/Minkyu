using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Minkyu.Manager;
using QuestPatcher.Axml;

var sourceApk = Environment.GetEnvironmentVariable("MINKYU_TEST_GAME_APK");
var testRoot = Path.Combine(Path.GetTempPath(), "MinkyuApkTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var results = new List<object>();
var failures = 0;
void Assert(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}
async Task Test(string name, Func<Task> run)
{
    try
    {
        await run();
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
object? Invoke(string name, params object[] args)
{
    try
    {
        return typeof(ApkPreparation).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
    }
    catch (TargetInvocationException ex)
    {
        throw ex.InnerException!;
    }
}
void Reject(Action action, string? expectedMessage = null)
{
    try
    {
        action();
    }
    catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException)
    {
        Assert(expectedMessage == null || ex.Message.Contains(expectedMessage, StringComparison.Ordinal), "Unexpected rejection: " + ex.Message);
        return;
    }
    throw new Exception("Expected rejection.");
}
AxmlElement Manifest(string package = "com.prpr.musedash", int version = 626)
{
    var manifest = new AxmlElement("manifest");
    var android = new Uri("http://schemas.android.com/apk/res/android");
    manifest.DeclaredNamespaces.Add("android", android);
    manifest.Attributes.Add(new AxmlAttribute("package", null, null, package));
    manifest.Attributes.Add(new AxmlAttribute("versionCode", android, 16843291, version));
    manifest.Children.Add(new AxmlElement("application"));
    return manifest;
}
await Test("bundled assets remove obsolete unlock mods and preserve unrelated mods", () =>
{
    var root = Path.Combine(testRoot, "upgrade");
    Directory.CreateDirectory(Path.Combine(root, "Mods"));
    var previous = Path.Combine(root, "Mods/Minkyu.LocalUnlock.dll");
    var renamed = Path.Combine(root, "Mods/MuseDashUnlockDLC.dll");
    File.WriteAllText(renamed, "previous renamed mod");
    var unrelated = Path.Combine(root, "Mods/OtherMod.dll");
    File.WriteAllText(previous, "previous mod");
    File.WriteAllText(unrelated, "user mod");
    var bytes = new byte[] { 1, 2, 3 };
    var copied = Minkyu.LoaderFixes.BundledAssets.CopyTo(root, _ => bytes);
    Assert(copied == 7, "Bundled dependency count changed.");
    Assert(!File.Exists(renamed), "Unlock mod still exists after upgrade.");
    Assert(!File.Exists(previous), "Old mod would still be loaded alongside the new name.");
    Assert(File.ReadAllText(unrelated) == "user mod", "Unrelated mod changed.");
    Assert(Minkyu.LoaderFixes.BundledAssets.CopyTo(root, _ => bytes) == 0, "Unchanged files should be reused.");
    File.WriteAllText(previous, "previous mod");
    Reject(() => Minkyu.LoaderFixes.BundledAssets.CopyTo(root, _ => Array.Empty<byte>()));
    Assert(File.Exists(previous), "Failed upgrade removed the previous mod.");
    return Task.CompletedTask;
});
await Test("APK identity, split and game hash checks reject unsupported inputs", () =>
{
    foreach (var kind in new[] { "package", "version", "split", "loader", "arm64", "hash" })
    {
        var path = Path.Combine(testRoot, kind + ".apk");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var manifest = Manifest(kind == "package" ? "wrong.package" : "com.prpr.musedash", kind == "version" ? 625 : 626);
            if (kind == "split")
                manifest.Attributes.Add(new AxmlAttribute("split", null, null, "config.arm64_v8a"));
            using (var output = zip.CreateEntry("AndroidManifest.xml").Open())
                AxmlSaver.SaveDocument(output, manifest);
            if (kind != "arm64")
                using (var output = zip.CreateEntry("lib/arm64-v8a/libil2cpp.so").Open())
                    output.Write(new byte[] { 1, 2, 3 });
            if (kind == "loader")
                zip.CreateEntry("assets/lemon_patch_date.txt");
        }
        using var archive = ZipFile.OpenRead(path);
        var entries = Invoke("IndexEntries", archive, "fixture")!;
        var expected = kind switch
        {
            "package" or "version" => "Only Muse Dash",
            "split" => "Split APKs",
            "loader" => "already contains a mod loader",
            "arm64" => "no ARM64 game library",
            _ => "Unsupported or damaged APK member"
        };
        Reject(() => Invoke("ValidateSource", entries, (Action<string>)(_ => { }), CancellationToken.None), expected);
    }
    return Task.CompletedTask;
});
await Test("signer reads complete fields and rejects truncated input", () =>
{
    using var stream = new ShortReads(new byte[] { 1, 2, 3, 4 });
    using var memory = new MelonLoader.Installer.Core.Utilities.Signing.FileMemory(stream);
    Assert(memory.ReadBytes(4).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Partial stream read was accepted.");
    try
    {
        memory.ReadBytes(1);
        throw new Exception("Truncated field accepted.");
    }
    catch (EndOfStreamException) { }
    return Task.CompletedTask;
});
await Test("persistent key generation and stable reuse", () =>
{
    var keyPath = Path.Combine(testRoot, "patch-key.pem");
    var first = (string)Invoke("ReadOrCreateSigningKey", keyPath, (Action<string>)Console.WriteLine)!;
    var second = (string)Invoke("ReadOrCreateSigningKey", keyPath, (Action<string>)Console.WriteLine)!;
    Assert(first == second && File.ReadAllText(keyPath) == first, "Key changed between calls.");
    Invoke("ValidateKey", first);
    File.WriteAllText(keyPath, "broken key");
    Reject(() => Invoke("ReadOrCreateSigningKey", keyPath, (Action<string>)Console.WriteLine));
    Assert(File.ReadAllText(keyPath) == "broken key", "Corrupted saved key silently replaced.");
    return Task.CompletedTask;
});
await Test("binary manifest roundtrip preserves identity and limits permissions", () =>
{
    var manifest = Manifest();
    var sourceMic = manifest.Children.Count(e => e.Name == "uses-permission" && e.Attributes.Any(a => a.Value as string == "android.permission.RECORD_AUDIO"));
    Invoke("PatchManifest", manifest);
    using var patched = new MemoryStream();
    AxmlSaver.SaveDocument(patched, manifest);
    patched.Position = 0;
    var output = AxmlLoader.LoadDocument(patched);
    Assert(output.Attributes.Single(a => a.Name == "package").Value as string == "com.prpr.musedash", "Package changed.");
    var app = output.Children.Single(e => e.Name == "application");
    foreach (var flag in new[] { "debuggable", "extractNativeLibs", "requestLegacyExternalStorage" })
        Assert(app.Attributes.Single(a => a.Name == flag).Value is true, "Missing flag " + flag);
    Assert(output.Children.Count(e => e.Name == "uses-permission" && e.Attributes.Any(a => a.Value as string == "android.permission.RECORD_AUDIO")) == sourceMic, "Microphone permission added.");
    Assert(app.Children.Any(e => e.Name == "meta-data" && e.Attributes.Any(a => a.Value as string == "minkyu.prepared")), "Missing manager marker.");
    return Task.CompletedTask;
});
await Test("unsafe payload paths rejected", () =>
{
    foreach (var path in new[] { "/absolute", "../escape", "assets/../x", "assets//x", "C:/x", "assets\\x" })
        Reject(() => Invoke("ValidateEntryPath", path));
    return Task.CompletedTask;
});
if (!string.IsNullOrEmpty(sourceApk))
    await Test("actual supplied APK package architecture and native hashes validate", () =>
    {
        var source = sourceApk;
        using var zip = ZipFile.OpenRead(source);
        var entries = Invoke("IndexEntries", zip, "host test source")!;
        var manifest = (AxmlElement)Invoke("ValidateSource", entries, (Action<string>)Console.WriteLine, CancellationToken.None)!;
        Assert(manifest.Name == "manifest", "Manifest not returned.");
        return Task.CompletedTask;
    });
if (!string.IsNullOrEmpty(sourceApk) && args.Length > 1 && Directory.Exists(args[1]))
{
    var assets = Path.GetFullPath(args[1]);
    var source = sourceApk;
    var originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
    var files = Path.Combine(testRoot, "manager-files");
    Directory.CreateDirectory(files);
    var context = new Android.Content.Context(files, assets);
    await Test("prepare actual supplied APK with actual payload and preserve source", async () =>
    {
        var output = await ApkPreparation.PrepareAsync(context, source, Console.WriteLine);
        Assert(File.Exists(output), "Output missing.");
        Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) == originalHash, "Source changed.");
        Console.WriteLine("PREPARED_APK " + output);
    });
    await Test("cancelled preparation preserves previous output", async () =>
    {
        var output = Path.Combine(files, "prepared", "MuseDash-Minkyu.apk");
        var previous = SHA256.HashData(File.ReadAllBytes(output));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await ApkPreparation.PrepareAsync(context, source, Console.WriteLine, cancellation.Token);
            throw new Exception("Cancellation ignored.");
        }
        catch (OperationCanceledException) { }
        Assert(SHA256.HashData(File.ReadAllBytes(output)).SequenceEqual(previous), "Cancellation changed previous APK.");
    });
    await Test("already patched APK rejected without replacing previous output", async () =>
    {
        var output = Path.Combine(files, "prepared", "MuseDash-Minkyu.apk");
        var previous = SHA256.HashData(File.ReadAllBytes(output));
        var copy = Path.Combine(testRoot, "already-patched.apk");
        File.Copy(output, copy);
        try
        {
            await ApkPreparation.PrepareAsync(context, copy, Console.WriteLine);
            throw new Exception("Patched APK accepted.");
        }
        catch (InvalidDataException ex) { Assert(ex.Message.Contains("already contains"), "Unexpected rejection: " + ex); }
        Assert(SHA256.HashData(File.ReadAllBytes(output)).SequenceEqual(previous), "Rejection changed previous APK.");
    });
}
var report = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "apk-tests.json");
Directory.CreateDirectory(Path.GetDirectoryName(report)!);
File.WriteAllText(report, JsonSerializer.Serialize(new { passed = results.Count - failures, failed = failures, androidRuntimeTested = false, tests = results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT passed={results.Count - failures} failed={failures}");
Directory.Delete(testRoot, true);
return failures == 0 ? 0 : 1;
sealed class ShortReads(byte[] bytes) : MemoryStream(bytes)
{
    public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
    public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);
}
