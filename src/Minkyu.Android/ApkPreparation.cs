using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MelonLoader.Installer.Core;
using MelonLoader.Installer.Core.Utilities.Signing;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using QuestPatcher.Axml;

namespace Minkyu.Manager;

internal static class ApkPreparation
{
    internal static bool HasCurrentPreparedApk(global::Android.Content.Context context)
    {
        var directory = Path.Combine(context.FilesDir!.AbsolutePath, "prepared");
        var marker = Path.Combine(directory, "payload.sha256");
        if (!File.Exists(Path.Combine(directory, "MuseDash-Minkyu.apk")) || !File.Exists(marker))
            return false;
        using var asset = context.Assets!.Open("loader-payload.json");
        return File.ReadAllText(marker).Trim() == Hex(SHA256.HashData(asset));
    }

    private const string Package = "com.prpr.musedash";
    private const int VersionCode = 626;
    private const string Il2CppPath = "lib/arm64-v8a/libil2cpp.so";
    private const string MetadataPath = "assets/bin/Data/Managed/Metadata/global-metadata.dat";
    private const string BootstrapPath = "assets/minkyu-bootstrap.dex";
    private const string Il2CppSha = "e9713ee0a96778e0e24243b7bfac67302bf351ae6e246f5d821e58c41759e2f3";
    private const string MetadataSha = "fa49bf33f529c28d159e16c29bbf2daeef3585dc533e0b4628dd9b9f62a251fd";
    private const long MaxEntryBytes = 512L * 1024 * 1024;
    private const long MaxArchiveBytes = 2L * 1024 * 1024 * 1024 - 1;
    private static readonly Uri AndroidNamespace = new("http://schemas.android.com/apk/res/android");
    private static readonly SemaphoreSlim PreparationLock = new(1, 1);
    private static readonly HashSet<string> NativeOverrides = new(StringComparer.Ordinal)
    {
        "lib/arm64-v8a/libBootstrap.so",
        "lib/arm64-v8a/libcrypto.so",
        "lib/arm64-v8a/libdobby.so",
        "lib/arm64-v8a/libmain.so",
        "lib/arm64-v8a/libssl.so",
        "lib/arm64-v8a/libunity.so"
    };
    internal static async Task<string> PrepareAsync(global::Android.Content.Context context, string sourceApk, Action<string> log, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(log);
        var app = context.ApplicationContext ?? context;
        var files = app.FilesDir?.AbsolutePath ?? throw new IOException("Manager private storage is unavailable.");
        await PreparationLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Prepare(app, files, sourceApk, log, cancellation), cancellation).ConfigureAwait(false);
        }
        finally
        {
            PreparationLock.Release();
        }
    }

    private static string Prepare(global::Android.Content.Context context, string files, string sourceApk, Action<string> log, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(sourceApk);
        var prepared = Path.Combine(files, "prepared");
        var destination = Path.Combine(prepared, "MuseDash-Minkyu.apk");
        if (string.Equals(source, destination, StringComparison.Ordinal))
            throw new InvalidOperationException("Select the supplied game APK, not the prepared output.");
        if (!File.Exists(source))
            throw new FileNotFoundException("Select a readable, standalone Muse Dash APK.", source);
        if (new FileInfo(source).Length > MaxArchiveBytes)
            throw new InvalidDataException("APK exceeds this signer's supported size.");
        var temporaryRoot = Path.Combine(files, "preparation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            log("Checking the supplied APK and the local loader payload...");
            var payloadPath = Path.Combine(temporaryRoot, "loader-payload.zip");
            using (var asset = context.Assets!.Open("loader-payload.zip"))
            using (var output = new FileStream(payloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                CopyChecked(asset, output, MaxArchiveBytes, null, cancellation);
            Payload description;
            using (var asset = context.Assets!.Open("loader-payload.json"))
                description = ReadPayload(asset, cancellation);
            var unsigned = Path.Combine(temporaryRoot, "prepared.apk");
            using (var sourceFile = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var original = new ZipArchive(sourceFile, ZipArchiveMode.Read))
            using (var payload = ZipFile.OpenRead(payloadPath))
            {
                var sourceEntries = IndexEntries(original, "source APK");
                var payloadEntries = IndexEntries(payload, "loader payload");
                ValidatePayloadEntries(description, payloadEntries);
                var manifest = ValidateSource(sourceEntries, log, cancellation);
                PatchManifest(manifest);
                cancellation.ThrowIfCancellationRequested();
                log("Copying game files and adding the verified loader (source APK stays unchanged)...");
                using (var outputFile = new FileStream(unsigned, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var output = new ZipArchive(outputFile, ZipArchiveMode.Create))
                {
                    long total = 0;
                    foreach (var entry in sourceEntries.Values)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (entry.FullName.EndsWith('/') || IsSignature(entry.FullName) || entry.FullName == "AndroidManifest.xml" || description.Files.ContainsKey(entry.FullName))
                            continue;
                        total = CheckedTotal(total, entry.Length);
                        using var input = entry.Open();
                        // Android needs the resource table uncompressed and aligned to 4 bytes.
                        var compression = entry.FullName == "resources.arsc" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                        using var target = output.CreateEntry(entry.FullName, compression).Open();
                        var copied = CopyChecked(input, target, entry.Length, null, cancellation);
                        if (copied != entry.Length)
                            throw new InvalidDataException("Incomplete source APK member: " + entry.FullName);
                    }

                    using (var target = output.CreateEntry("AndroidManifest.xml", CompressionLevel.Optimal).Open())
                        AxmlSaver.SaveDocument(target, manifest);
                    foreach (var file in description.Files.Values.OrderBy(f => f.Path, StringComparer.Ordinal))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        total = CheckedTotal(total, file.Size);
                        using var input = payloadEntries[file.Path].Open();
                        using var target = output.CreateEntry(file.Path, CompressionLevel.Optimal).Open();
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var copied = CopyChecked(input, target, file.Size, hash, cancellation);
                        if (copied != file.Size || Hex(hash.GetHashAndReset()) != file.Sha256)
                            throw new InvalidDataException("Loader payload integrity check failed: " + file.Path);
                    }
                    var bootstrap = description.Files[BootstrapPath];
                    total = CheckedTotal(total, bootstrap.Size);
                    using var bootstrapInput = payloadEntries[BootstrapPath].Open();
                    using var bootstrapOutput = output.CreateEntry(BootstrapDexPath(sourceEntries), CompressionLevel.Optimal).Open();
                    using var bootstrapHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    if (CopyChecked(bootstrapInput, bootstrapOutput, bootstrap.Size, bootstrapHash, cancellation) != bootstrap.Size
                        || Hex(bootstrapHash.GetHashAndReset()) != bootstrap.Sha256)
                        throw new InvalidDataException("Game bootstrap integrity check failed.");
                }
            }

            cancellation.ThrowIfCancellationRequested();
            var key = ReadOrCreateSigningKey(Path.Combine(files, "patch-key.pem"), log);
            cancellation.ThrowIfCancellationRequested();
            log("Signing with this manager installation's private key. Signing finishes before cancellation is applied.");
            new APKSigner(key, new PatchLogger(log)).Sign(unsigned);
            cancellation.ThrowIfCancellationRequested();
            VerifyPreparedEntries(unsigned, description, cancellation);
            Directory.CreateDirectory(prepared);
            File.Move(unsigned, destination, true);
            using (var asset = context.Assets!.Open("loader-payload.json"))
            {
                var marker = Path.Combine(prepared, "payload.sha256");
                File.WriteAllText(marker + ".pending", Hex(SHA256.HashData(asset)));
                File.Move(marker + ".pending", marker, true);
            }

            log("Prepared Muse Dash locally. Its signing certificate differs from the supplied game; Android may require backing up and removing the installed copy before installation.");
            return destination;
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryRoot, true);
            }
            catch (Exception ex)when (ex is IOException or UnauthorizedAccessException)
            {
                log("Temporary preparation files could not be removed: " + ex.Message);
            }
        }
    }

    private sealed record PayloadFile(string Path, string Sha256, long Size);
    private sealed record Payload(Dictionary<string, PayloadFile> Files);
    private static Payload ReadPayload(Stream stream, CancellationToken cancellation)
    {
        using var bytes = new MemoryStream();
        CopyChecked(stream, bytes, 2 * 1024 * 1024, null, cancellation);
        using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 12 });
        var root = json.RootElement;
        RequireUniqueFields(root);
        var source = root.GetProperty("source");
        RequireUniqueFields(source);
        if (source.GetProperty("package").GetString() != Package || source.GetProperty("versionCode").GetInt32() != VersionCode || ReadSha(source.GetProperty("libil2cppSha256")) != Il2CppSha || ReadSha(source.GetProperty("globalMetadataSha256")) != MetadataSha)
            throw new InvalidDataException("Loader payload targets a different Muse Dash build.");
        var list = root.GetProperty("files");
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 20000)
            throw new InvalidDataException("Loader payload has an invalid file list.");
        var files = new Dictionary<string, PayloadFile>(StringComparer.Ordinal);
        long total = 0;
        foreach (var item in list.EnumerateArray())
        {
            RequireUniqueFields(item);
            var path = item.GetProperty("path").GetString() ?? throw new InvalidDataException("Missing loader payload path.");
            ValidateEntryPath(path);
            if (!(path.StartsWith("assets/MelonLoader/", StringComparison.Ordinal) || path.StartsWith("assets/dotnet/", StringComparison.Ordinal) || path.StartsWith("assets/copyToBase/", StringComparison.Ordinal) || path == "assets/lemon_patch_date.txt" || path == BootstrapPath || NativeOverrides.Contains(path)))
                throw new InvalidDataException("Loader payload may not replace game content: " + path);
            var size = item.GetProperty("size").GetInt64();
            if (size < 0 || size > MaxEntryBytes)
                throw new InvalidDataException("Invalid loader payload member size.");
            total = CheckedTotal(total, size);
            if (!files.TryAdd(path, new PayloadFile(path, ReadSha(item.GetProperty("sha256")), size)))
                throw new InvalidDataException("Duplicate loader payload path: " + path);
        }

        foreach (var required in NativeOverrides.Concat(new[] { "assets/MelonLoader/net8/MelonLoader.dll", "assets/copyToBase/Mods/CustomAlbums.Android.dll", BootstrapPath }))
            if (!files.ContainsKey(required))
                throw new InvalidDataException("Loader payload is incomplete: " + required);
        return new Payload(files);
    }

    private static Dictionary<string, ZipArchiveEntry> IndexEntries(ZipArchive archive, string label)
    {
        if (archive.Entries.Count > 20000)
            throw new InvalidDataException(label + " has too many ZIP members.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateEntryPath(entry.FullName);
            if (entry.Length > MaxEntryBytes)
                throw new InvalidDataException(label + " contains an oversized member.");
            total = CheckedTotal(total, entry.Length);
            if (!entries.TryAdd(entry.FullName, entry))
                throw new InvalidDataException(label + " contains duplicate ZIP members.");
        }

        return entries;
    }

    private static void ValidatePayloadEntries(Payload payload, Dictionary<string, ZipArchiveEntry> entries)
    {
        var files = entries.Values.Where(e => !e.FullName.EndsWith('/')).ToArray();
        if (files.Length != payload.Files.Count)
            throw new InvalidDataException("Loader payload ZIP differs from its manifest.");
        foreach (var file in files)
            if (!payload.Files.TryGetValue(file.FullName, out var expected) || file.Length != expected.Size)
                throw new InvalidDataException("Unexpected loader payload ZIP member: " + file.FullName);
    }

    private static AxmlElement ValidateSource(Dictionary<string, ZipArchiveEntry> entries, Action<string> log, CancellationToken cancellation)
    {
        if (entries.Keys.Any(path => path.StartsWith("assets/MelonLoader/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("assets/dotnet/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("assets/copyToBase/", StringComparison.OrdinalIgnoreCase) || path.Equals("assets/lemon_patch_date.txt", StringComparison.OrdinalIgnoreCase) || path.Equals("lib/arm64-v8a/libBootstrap.so", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("This APK already contains a mod loader. Select the supplied APK before Minkyu/LemonLoader patching.");
        if (!entries.TryGetValue("AndroidManifest.xml", out var manifestEntry))
            throw new InvalidDataException("No AndroidManifest.xml found. Select a standalone APK, not an APKS/XAPK bundle.");
        if (manifestEntry.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("APK manifest is too large.");
        using var manifestBytes = new MemoryStream();
        using (var stream = manifestEntry.Open())
            CopyChecked(stream, manifestBytes, 4 * 1024 * 1024, null, cancellation);
        manifestBytes.Position = 0;
        var manifest = AxmlLoader.LoadDocument(manifestBytes);
        if (manifest.Name != "manifest")
            throw new InvalidDataException("Invalid Android manifest root.");
        if (manifest.Attributes.Any(a => a.Name is "split" or "isFeatureSplit" or "requiredSplitTypes") || manifest.Children.Any(e => e.Name == "uses-split") || manifest.Children.Where(e => e.Name == "application").Any(e => e.Attributes.Any(a => a.Name == "isSplitRequired" && a.Value is true) || e.Children.Any(child => child.Name == "meta-data" && child.Attributes.Any(a => a.Name == "name" && a.Value as string == "com.android.vending.splits.required") && child.Attributes.Any(a => a.Name == "value" && a.Value is true))))
            throw new InvalidDataException("Split APKs are not supported. Supply the complete standalone Muse Dash 6.7.0 build 626 APK.");
        var package = manifest.Attributes.SingleOrDefault(a => a.Name == "package" && a.Namespace == null)?.Value as string;
        var version = manifest.Attributes.SingleOrDefault(a => a.Name == "versionCode" && a.Namespace == AndroidNamespace)?.Value;
        if (package != Package || version is not int code || code != VersionCode)
            throw new InvalidDataException("Only Muse Dash com.prpr.musedash 6.7.0 (build 626) is supported by this patch.");
        if (!entries.ContainsKey(Il2CppPath))
            throw new InvalidDataException("The APK has no ARM64 game library. Split/base-only APKs are unsupported; supply the complete standalone build 626 APK.");
        VerifyMember(entries, Il2CppPath, Il2CppSha, cancellation);
        VerifyMember(entries, MetadataPath, MetadataSha, cancellation);
        log("Confirmed package, build 626, ARM64 IL2CPP, and matching game metadata.");
        return manifest;
    }

    private static void PatchManifest(AxmlElement manifest)
    {
        manifest.DeclaredNamespaces.TryAdd("android", AndroidNamespace);
        foreach (var permission in new[]
        {
            "android.permission.READ_EXTERNAL_STORAGE",
            "android.permission.WRITE_EXTERNAL_STORAGE",
            "android.permission.MANAGE_EXTERNAL_STORAGE"
        }
        )
        {
            if (manifest.Children.Any(e => e.Name == "uses-permission" && e.Attributes.Any(a => a.Name == "name" && a.Namespace == AndroidNamespace && a.Value as string == permission)))
                continue;
            var node = new AxmlElement("uses-permission");
            node.Attributes.Add(new AxmlAttribute("name", AndroidNamespace, 16842755, permission));
            manifest.Children.Add(node);
        }

        var application = manifest.Children.Single(e => e.Name == "application");
        void Flag(string name, int resource)
        {
            var attribute = application.Attributes.SingleOrDefault(a => a.Name == name && a.Namespace == AndroidNamespace);
            if (attribute == null)
                application.Attributes.Add(new AxmlAttribute(name, AndroidNamespace, resource, true));
            else
            {
                attribute.Value = true;
                attribute.ResourceId = resource;
            }
        }

        Flag("debuggable", 16842767);
        Flag("extractNativeLibs", 16844010);
        Flag("requestLegacyExternalStorage", 16844291);
        var bootstrap = new AxmlElement("activity");
        bootstrap.Attributes.Add(new AxmlAttribute("name", AndroidNamespace, 16842755, "app.minkyu.bootstrap.GameActivity"));
        bootstrap.Attributes.Add(new AxmlAttribute("exported", AndroidNamespace, 16842768, true));
        bootstrap.Attributes.Add(new AxmlAttribute("screenOrientation", AndroidNamespace, 16842782, 0));
        bootstrap.Attributes.Add(new AxmlAttribute("configChanges", AndroidNamespace, 16842783, 0x80 | 0x400));
        foreach (var activity in application.Children.Where(e => e.Name is "activity" or "activity-alias").ToArray())
            foreach (var filter in activity.Children.Where(e => e.Name == "intent-filter"
                && e.Children.Any(c => c.Name == "category" && c.Attributes.Any(a => a.Name == "name" && a.Value as string == "android.intent.category.LAUNCHER"))).ToArray())
            {
                activity.Children.Remove(filter);
                bootstrap.Children.Add(filter);
            }
        if (bootstrap.Children.Count > 0) application.Children.Add(bootstrap);
        var marker = application.Children.FirstOrDefault(e => e.Name == "meta-data" && e.Attributes.Any(a => a.Name == "name" && a.Namespace == AndroidNamespace && a.Value as string == "minkyu.prepared"));
        if (marker == null)
        {
            marker = new AxmlElement("meta-data");
            marker.Attributes.Add(new AxmlAttribute("name", AndroidNamespace, 16842755, "minkyu.prepared"));
            marker.Attributes.Add(new AxmlAttribute("value", AndroidNamespace, 16842788, true));
            application.Children.Add(marker);
        }
    }

    private static void VerifyPreparedEntries(string path, Payload payload, CancellationToken cancellation)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = IndexEntries(archive, "prepared APK");
        VerifyMember(entries, Il2CppPath, Il2CppSha, cancellation);
        VerifyMember(entries, MetadataPath, MetadataSha, cancellation);
        foreach (var file in payload.Files.Values)
            VerifyMember(entries, file.Path, file.Sha256, cancellation);
        var dex = entries.Keys.Where(p => p.StartsWith("classes", StringComparison.Ordinal) && p.EndsWith(".dex", StringComparison.Ordinal))
            .Any(p => {
                using var stream = entries[p].Open();
                return Hex(SHA256.HashData(stream)) == payload.Files[BootstrapPath].Sha256;
            });
        if (!dex) throw new InvalidDataException("Prepared game is missing the startup activity.");
        using var fileStream = File.OpenRead(path);
        var tailLength = (int)Math.Min(65557, fileStream.Length);
        var tail = new byte[tailLength];
        fileStream.Position = fileStream.Length - tailLength;
        fileStream.ReadExactly(tail);
        var eocd = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
            if (BitConverter.ToUInt32(tail, i) == 0x06054b50 && i + 22 + BitConverter.ToUInt16(tail, i + 20) == tail.Length)
            {
                eocd = i;
                break;
            }

        if (eocd < 0)
            throw new InvalidDataException("Prepared APK has no valid ZIP footer.");
        var centralDirectory = BitConverter.ToUInt32(tail, eocd + 16);
        VerifyResourceTableLayout(fileStream, centralDirectory);
        if (centralDirectory < 24)
            throw new InvalidDataException("Prepared APK has no signing block.");
        fileStream.Position = centralDirectory - 16;
        var magic = new byte[16];
        fileStream.ReadExactly(magic);
        if (Encoding.ASCII.GetString(magic) != "APK Sig Block 42")
            throw new InvalidDataException("APK v2 signing block was not written.");
    }

    private static void VerifyResourceTableLayout(FileStream file, uint centralDirectory)
    {
        file.Position = centralDirectory;
        var header = new byte[46];
        while (file.Position + header.Length <= file.Length)
        {
            file.ReadExactly(header);
            if (BitConverter.ToUInt32(header, 0) != 0x02014b50)
                break;
            var nameLength = BitConverter.ToUInt16(header, 28);
            var extraLength = BitConverter.ToUInt16(header, 30);
            var commentLength = BitConverter.ToUInt16(header, 32);
            var name = new byte[nameLength];
            file.ReadExactly(name);
            if (Encoding.UTF8.GetString(name) == "resources.arsc")
            {
                if (BitConverter.ToUInt16(header, 10) != 0)
                    throw new InvalidDataException("Resource table must remain uncompressed for Android installation.");
                var offset = BitConverter.ToUInt32(header, 42);
                file.Position = offset;
                var local = new byte[30];
                file.ReadExactly(local);
                if (BitConverter.ToUInt32(local, 0) != 0x04034b50 || BitConverter.ToUInt16(local, 8) != 0)
                    throw new InvalidDataException("Resource table local ZIP header is invalid.");
                var dataOffset = (long)offset + 30 + BitConverter.ToUInt16(local, 26) + BitConverter.ToUInt16(local, 28);
                if (dataOffset % 4 != 0)
                    throw new InvalidDataException("Resource table must be aligned to four bytes for Android installation.");
                return;
            }

            file.Position += extraLength + commentLength;
        }

        throw new InvalidDataException("Prepared game is missing its Android resource table.");
    }

    private static string ReadOrCreateSigningKey(string path, Action<string> log)
    {
        if (File.Exists(path))
        {
            // Keep this key so existing game installs can still be updated.
            var pem = File.ReadAllText(path);
            ValidateKey(pem);
            return pem;
        }

        log("Creating a private signing key for this manager installation. Keep manager data to preserve update compatibility.");
        var random = new SecureRandom();
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(random, 2048));
        var pair = generator.GenerateKeyPair();
        var certificate = new X509V3CertificateGenerator();
        var name = new X509Name("CN=Minkyu Local APK Preparation");
        certificate.SetSerialNumber(new Org.BouncyCastle.Math.BigInteger(160, random).Abs().Add(Org.BouncyCastle.Math.BigInteger.One));
        certificate.SetIssuerDN(name);
        certificate.SetSubjectDN(name);
        certificate.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        certificate.SetNotAfter(DateTime.UtcNow.AddYears(30));
        certificate.SetPublicKey(pair.Public);
        var cert = certificate.Generate(new Asn1SignatureFactory("SHA256WITHRSA", pair.Private, random));
        using var text = new StringWriter();
        var writer = new PemWriter(text);
        writer.WriteObject(pair);
        writer.WriteObject(cert);
        writer.Writer.Flush();
        var generated = text.ToString();
        ValidateKey(generated);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.ASCII.GetBytes(generated);
                stream.Write(bytes);
                stream.Flush(true);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, false);
            return generated;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void ValidateKey(string pem)
    {
        using var text = new StringReader(pem);
        var reader = new PemReader(text);
        X509Certificate? certificate = null;
        AsymmetricCipherKeyPair? pair = null;
        object? item;
        while ((item = reader.ReadObject()) != null)
        {
            if (item is X509Certificate cert)
                certificate ??= cert;
            if (item is AsymmetricCipherKeyPair keys)
                pair ??= keys;
        }

        if (certificate?.GetPublicKey() is not RsaKeyParameters publicKey || pair?.Private is not RsaKeyParameters privateKey || !publicKey.Modulus.Equals(privateKey.Modulus) || privateKey.Modulus.BitLength < 2048)
            throw new InvalidDataException("The saved manager signing key is invalid. Restore its backup; a replacement key cannot update previous builds.");
        certificate.Verify(certificate.GetPublicKey());
    }

    private static void VerifyMember(Dictionary<string, ZipArchiveEntry> entries, string path, string expected, CancellationToken cancellation)
    {
        if (!entries.TryGetValue(path, out var entry))
            throw new InvalidDataException("Required APK member missing: " + path);
        using var stream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        CopyChecked(stream, Stream.Null, entry.Length, hash, cancellation);
        if (Hex(hash.GetHashAndReset()) != expected)
            throw new InvalidDataException("Unsupported or damaged APK member: " + path);
    }

    private static long CopyChecked(Stream input, Stream output, long maximum, IncrementalHash? hash, CancellationToken cancellation)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (count > maximum - total)
                throw new InvalidDataException("APK or payload content exceeds its size limit.");
            hash?.AppendData(buffer, 0, count);
            output.Write(buffer, 0, count);
            total += count;
        }

        return total;
    }

    private static long CheckedTotal(long total, long size) => size < 0 || size > MaxArchiveBytes - total ? throw new InvalidDataException("APK exceeds this signer's supported ZIP size.") : total + size;
    private static string ReadSha(JsonElement value)
    {
        var text = value.GetString();
        if (text?.Length != 64 || text.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Invalid payload SHA-256.");
        return text.ToLowerInvariant();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    private static string BootstrapDexPath(Dictionary<string, ZipArchiveEntry> entries)
    {
        var index = 2;
        while (entries.ContainsKey($"classes{index}.dex")) index++;
        return $"classes{index}.dex";
    }
    private static bool IsSignature(string path) => path.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase);
    private static void ValidateEntryPath(string path)
    {
        var parts = path.TrimEnd('/').Split('/');
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.Contains('\\') || path.Contains(':') || path.Contains('\0') || parts.Any(p => p.Length == 0 || p is "." or ".."))
            throw new InvalidDataException("Unsafe APK ZIP member path.");
    }

    private static void RequireUniqueFields(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Payload manifest must contain JSON objects.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
            if (!fields.Add(field.Name))
                throw new InvalidDataException("Duplicate payload JSON field.");
    }

    private sealed class PatchLogger(Action<string> log) : IPatchLogger
    {
        public void Log(string message) => log(message);
    }
}
