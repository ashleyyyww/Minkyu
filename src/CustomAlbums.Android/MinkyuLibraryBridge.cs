using System.Security.Cryptography;
using System.Text;
using CustomAlbums.Core;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CustomAlbums.Android;

internal static class MinkyuLibraryBridge
{
    internal const string Authority = "app.minkyu.manager.library";
    internal const string CatalogUri = "content://" + Authority + "/catalog";
    internal const string ExpansionUri = "content://" + Authority + "/expansion";
    internal const int MaxCatalogBytes = 1024 * 1024;
    internal const int MaxAlbums = 256;
    internal const long MaxExpansionBytes = 4L * 1024 * 1024 * 1024;
    private const string ExpansionName = "main.626.com.prpr.musedash.obb";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal sealed record Catalog(string Session, IReadOnlyList<Entry> Albums);
    internal sealed record Entry(string Id, string Name, string Uri);
    internal static IReadOnlyList<string> Load(string customAlbumsRoot)
    {
        var root = Path.Combine(customAlbumsRoot, "MinkyuLibrary");
        try
        {
            using var objects = new JavaScope();
            var player = objects.Own(new AndroidJavaClass("com.unity3d.player.UnityPlayer"));
            var activity = objects.Own(player.GetStatic<AndroidJavaObject>("currentActivity"));
            if (activity == null)
                throw new InvalidOperationException("Unity activity is unavailable.");
            var intent = objects.Own(activity.Call<AndroidJavaObject>("getIntent"));
            if (intent == null)
                return ReadPersistedPaths(root);
            var expansion = intent.Call<string>("getStringExtra", (Il2CppSystem.String)"minkyu.expansion");
            var catalog = intent.Call<string>("getStringExtra", (Il2CppSystem.String)"minkyu.catalog");
            if (string.IsNullOrEmpty(expansion) && string.IsNullOrEmpty(catalog))
                return ReadPersistedPaths(root);
            var resolver = objects.Own(activity.Call<AndroidJavaObject>("getContentResolver"));
            if (resolver == null)
                throw new InvalidOperationException("Android content resolver is unavailable.");
            if (!string.IsNullOrEmpty(expansion))
            {
                try
                {
                    if (!string.Equals(expansion, ExpansionUri, StringComparison.Ordinal))
                        throw new InvalidDataException("Unsupported expansion content URI.");
                    var sha = intent.Call<string>("getStringExtra", (Il2CppSystem.String)"minkyu.expansion.sha256");
                    var obbDir = objects.Own(activity.Call<AndroidJavaObject>("getObbDir"));
                    if (obbDir == null)
                        throw new IOException("Android expansion directory is unavailable.");
                    var directory = obbDir.Call<string>("getAbsolutePath");
                    ImportExpansion(directory, sha, () => OpenUri(resolver, ExpansionUri));
                }
                catch (Exception ex)
                {
                    Mod.Log.Error("CA_MINKYU_EXPANSION_FAILED " + ex.Message);
                }
            }

            if (!string.IsNullOrEmpty(catalog))
            {
                if (!string.Equals(catalog, CatalogUri, StringComparison.Ordinal))
                    throw new InvalidDataException("Unsupported manager catalog content URI.");
                var session = intent.Call<string>("getStringExtra", (Il2CppSystem.String)"minkyu.session");
                using var stream = OpenUri(resolver, CatalogUri);
                return Synchronize(root, stream, session, uri => OpenUri(resolver, uri));
            }
        }
        catch (Exception ex)
        {
            Mod.Log.Error("CA_MINKYU_SYNC_FAILED preserving previous library: " + ex.Message);
        }

        return ReadPersistedPaths(root);
    }

    internal static IReadOnlyList<string> Synchronize(string root, Stream catalogStream, string expectedSession, Func<string, Stream> openChart)
    {
        ValidateSession(expectedSession);
        var catalog = ReadCatalog(catalogStream);
        if (!string.Equals(catalog.Session, expectedSession, StringComparison.Ordinal))
            throw new InvalidDataException("Manager catalog session does not match launch session.");
        var library = new AlbumLibrary(Path.Combine(root, "Library"));
        var paths = new List<string>();
        foreach (var entry in catalog.Albums)
        {
            using var source = openChart(entry.Uri);
            var path = library.Import(source);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), entry.Id, StringComparison.Ordinal))
                throw new InvalidDataException("Manager chart bytes do not match the declared SHA-256.");
            paths.Add(path);
        }

        var manifest = new JObject
        {
            ["version"] = 1,
            ["session"] = catalog.Session,
            ["albums"] = new JArray(catalog.Albums.Select(e => new JObject { ["id"] = e.Id, ["name"] = e.Name, ["uri"] = e.Uri }))
        };
        AtomicWrite(Path.Combine(root, "catalog.json"), StrictUtf8.GetBytes(manifest.ToString(Formatting.None)));
        var active = new HashSet<string>(paths, StringComparer.Ordinal);
        var removed = 0;
        foreach (var cached in library.EnumeratePaths())
        {
            if (active.Contains(cached))
                continue;
            try
            {
                File.Delete(cached);
                removed++;
            }
            catch (Exception ex)
            {
                Mod.Log.Error("CA_MINKYU_CACHE_DELETE_FAILED " + Path.GetFileName(cached) + ": " + ex.Message);
            }
        }

        if (removed > 0)
            Mod.Log.Msg("CA_MINKYU_CACHE_REMOVED files=" + removed);
        Mod.Log.Msg("CA_MINKYU_SYNC_READY albums=" + paths.Count + " freshLaunchRequired=true");
        return paths;
    }

    internal static IReadOnlyList<string> ReadPersistedPaths(string root)
    {
        try
        {
            var manifest = Path.Combine(root, "catalog.json");
            if (!File.Exists(manifest))
                return Array.Empty<string>();
            using var stream = File.OpenRead(manifest);
            var catalog = ReadCatalog(stream);
            return catalog.Albums.Select(e => Path.Combine(root, "Library", e.Id + ".mdm")).Where(File.Exists).ToArray();
        }
        catch (Exception ex)
        {
            Mod.Log.Error("CA_MINKYU_PERSISTED_CATALOG_REJECTED " + ex.Message);
            return Array.Empty<string>();
        }
    }

    internal static Catalog ReadCatalog(Stream stream)
    {
        using var bytes = new MemoryStream();
        CopyBounded(stream, bytes, MaxCatalogBytes);
        using var text = new StringReader(StrictUtf8.GetString(bytes.ToArray()));
        using var reader = new JsonTextReader(text)
        {
            MaxDepth = 12,
            DateParseHandling = DateParseHandling.None
        };
        var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read())
            throw new InvalidDataException("Unexpected data after manager catalog.");
        if (json["version"]?.Type != JTokenType.Integer || (long)json["version"] != 1)
            throw new InvalidDataException("Unsupported manager catalog version.");
        var session = RequireString(json["session"], "session");
        ValidateSession(session);
        if (json["albums"] is not JArray albums || albums.Count > MaxAlbums)
            throw new InvalidDataException("Manager catalog must contain at most 256 albums.");
        var entries = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in albums)
        {
            if (token is not JObject album)
                throw new InvalidDataException("Invalid manager album record.");
            var id = ValidateId(RequireString(album["id"], "id"));
            if (!seen.Add(id))
                throw new InvalidDataException("Duplicate manager chart SHA-256.");
            var name = RequireString(album["name"], "name");
            if (name.Length > 4096)
                throw new InvalidDataException("Manager chart name is too long.");
            var uri = RequireString(album["uri"], "uri");
            if (!string.Equals(uri, "content://" + Authority + "/chart/" + id, StringComparison.Ordinal))
                throw new InvalidDataException("Unsupported manager chart content URI.");
            entries.Add(new Entry(id, name, uri));
        }

        return new Catalog(session, entries);
    }

    internal static void ImportExpansion(string directory, string expectedSha, Func<Stream> openExpansion)
    {
        var sha = ValidateId(expectedSha);
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("Android expansion directory must be absolute.");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, ExpansionName);
        if (Minkyu.Shared.LaunchFileVerification.Verify(destination, sha, MaxExpansionBytes, out _))
            return;

        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            long length;
            using (var source = openExpansion())
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                length = CopyBounded(source, output, MaxExpansionBytes);
                if (length == 0)
                    throw new InvalidDataException("Manager expansion is empty.");
                output.Flush(true);
            }

            using (var imported = File.OpenRead(temporary))
                if (Hash(imported) != sha)
                    throw new InvalidDataException("Manager expansion SHA-256 mismatch.");
            File.Move(temporary, destination, true);
            Mod.Log.Msg("CA_MINKYU_EXPANSION_READY reused=false bytes=" + length);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static FileStream OpenUri(AndroidJavaObject resolver, string uri)
    {
        using var objects = new JavaScope();
        var uriClass = objects.Own(new AndroidJavaClass("android.net.Uri"));
        var parsed = objects.Own(uriClass.CallStatic<AndroidJavaObject>("parse", (Il2CppSystem.String)uri));
        var parcel = objects.Own(resolver.Call<AndroidJavaObject>("openFileDescriptor", parsed, (Il2CppSystem.String)"r"));
        if (parcel == null)
            throw new IOException("Manager content provider returned no descriptor.");
        SafeFileHandle handle = null;
        try
        {
            var fd = parcel.Call<int>("detachFd");
            if (fd < 0)
                throw new IOException("Manager descriptor could not be detached.");
            handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle?.Dispose();
            throw;
        }
        finally
        {
            try
            {
                parcel.Call("close");
            }
            catch (Exception ex)
            {
                Mod.Log.Error("CA_MINKYU_DESCRIPTOR_CLOSE_FAILED " + ex.Message);
            }
        }
    }

    private static string RequireString(JToken token, string name) => token?.Type == JTokenType.String ? (string)token : throw new InvalidDataException("Manager catalog field must be a string: " + name);
    private static void ValidateSession(string session)
    {
        if (string.IsNullOrWhiteSpace(session) || session.Length > 256)
            throw new InvalidDataException("Missing or invalid manager session.");
    }

    private static string ValidateId(string id)
    {
        if (id == null || id.Length != 64 || id.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))))
            throw new InvalidDataException("Expected a 64-character SHA-256 identifier.");
        return id.ToLowerInvariant();
    }

    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    private static long CopyBounded(Stream source, Stream destination, long maximum)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (count > maximum - total)
                throw new InvalidDataException("Manager content exceeds size limit.");
            destination.Write(buffer, 0, count);
            total += count;
        }

        return total;
    }

    private static void AtomicWrite(string destination, byte[] bytes)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(true);
            }

            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private sealed class JavaScope : IDisposable
    {
        private readonly List<AndroidJavaObject> objects = new();
        internal T Own<T>(T value)
            where T : AndroidJavaObject
        {
            if (value != null)
                objects.Add(value);
            return value;
        }

        public void Dispose()
        {
            for (var i = objects.Count - 1; i >= 0; i--)
                try
                {
                    objects[i].Dispose();
                }
                catch (Exception ex)
                {
                    Mod.Log.Error("CA_MINKYU_JAVA_DISPOSE_FAILED " + ex.Message);
                }
        }
    }
}
