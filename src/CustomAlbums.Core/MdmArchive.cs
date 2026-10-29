using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomAlbums.Core
{
    public sealed class MdmArchive : IDisposable
    {
        private readonly ZipArchive zip;
        private readonly MdmReadOptions options;
        private readonly Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, uint> crc32 = new Dictionary<string, uint>(StringComparer.Ordinal);
        private bool disposed;
        private MdmArchive(FileStream stream, MdmReadOptions options)
        {
            this.options = options;
            using (var sha = SHA256.Create())
                Id = BmsParser.Hex(sha.ComputeHash(stream));
            var checksums = ValidateCentralDirectory(stream, options);
            stream.Position = 0;
            zip = new ZipArchive(stream, ZipArchiveMode.Read, false);
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (zip.Entries.Count > options.MaxEntries)
                    throw new InvalidDataException("Archive has too many entries.");
                long total = 0;
                for (var entryIndex = 0; entryIndex < zip.Entries.Count; entryIndex++)
                {
                    var entry = zip.Entries[entryIndex];
                    ValidateEntryName(entry.FullName);
                    if (!names.Add(entry.FullName))
                        throw new InvalidDataException("Duplicate or case-colliding ZIP entry: " + entry.FullName);
                    if (entry.Length > options.MaxEntryBytes)
                        throw new InvalidDataException("ZIP entry exceeds size limit: " + entry.FullName);
                    if (entry.Length > options.MaxTotalUncompressedBytes - total)
                        throw new InvalidDataException("ZIP uncompressed total exceeds limit.");
                    total += entry.Length;
                    if (entry.Length > 0 && (entry.CompressedLength == 0 || (decimal)entry.Length / entry.CompressedLength > options.MaxCompressionRatio))
                        throw new InvalidDataException("ZIP entry exceeds compression ratio limit: " + entry.FullName);
                    if (!entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        entries.Add(entry.FullName, entry);
                        crc32.Add(entry.FullName, checksums[entryIndex]);
                    }
                }

                Assets = entries.Values.Select(e => new MdmAsset { Name = e.FullName, Length = e.Length, CompressedLength = e.CompressedLength }).ToList().AsReadOnly();
                Metadata = ParseMetadata(ReadAssetBytes("info.json", options.MaxMetadataBytes));
                MusicEntry = entries.ContainsKey("music.ogg") ? "music.ogg" : entries.ContainsKey("music.mp3") ? "music.mp3" : "";
                if (MusicEntry.Length == 0)
                    throw new InvalidDataException("Missing root music.ogg or music.mp3.");
                DemoEntry = entries.ContainsKey("demo.ogg") ? "demo.ogg" : entries.ContainsKey("demo.mp3") ? "demo.mp3" : null;
                CoverEntry = entries.ContainsKey("cover.png") ? "cover.png" : null;
                if (options.RequireRecognizedAudioHeader)
                    CheckAudioHeader(MusicEntry);
                if (DemoEntry != null && options.RequireRecognizedAudioHeader)
                    CheckAudioHeader(DemoEntry);
                if (CoverEntry != null)
                {
                    var head = ReadPrefix(CoverEntry, 24);
                    var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
                    if (head.Length < 24 || !head.Take(8).SequenceEqual(png))
                        throw new InvalidDataException("cover.png has no PNG header.");
                    var width = (uint)head[16] << 24 | (uint)head[17] << 16 | (uint)head[18] << 8 | head[19];
                    var height = (uint)head[20] << 24 | (uint)head[21] << 16 | (uint)head[22] << 8 | head[23];
                    if (width == 0 || height == 0 || width > 8192 || height > 8192 || (ulong)width * height > 32UL * 1024 * 1024)
                        throw new InvalidDataException("cover.png dimensions exceed limits.");
                }

                var charts = new Dictionary<int, BmsChart>();
                var warnings = new List<string>();
                foreach (var difficulty in Metadata.Difficulties)
                {
                    var entryName = "map" + difficulty.Key + ".bms";
                    if (!entries.ContainsKey(entryName))
                    {
                        warnings.Add("Difficulty " + difficulty.Key + " is declared but " + entryName + " is missing; hidden from selection.");
                        continue;
                    }

                    charts.Add(difficulty.Key, BmsParser.Parse(ReadAssetBytes(entryName, options.MaxChartBytes), difficulty.Key, entryName, options.MaxEventsPerChart));
                }

                if (charts.Count == 0)
                    throw new InvalidDataException("No playable declared map1.bms through map5.bms.");
                Charts = Collections.ReadOnly(charts);
                foreach (var entry in entries.Keys)
                    if (entry.EndsWith(".talk", StringComparison.OrdinalIgnoreCase) || entry == "cinema.json" || entry.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        warnings.Add(entry + " is retained as an optional asset; playback requires a separate runtime adapter.");
                Warnings = warnings.AsReadOnly();
            }
            catch
            {
                zip.Dispose();
                throw;
            }
        }

        public string Id { get; }
        public AlbumMetadata Metadata { get; } = new AlbumMetadata();
        public IReadOnlyDictionary<int, BmsChart> Charts { get; } = new Dictionary<int, BmsChart>();
        public IReadOnlyList<MdmAsset> Assets { get; } = new MdmAsset[0];
        public IReadOnlyList<string> Warnings { get; } = new string[0];
        public string MusicEntry { get; } = "";
        public string? DemoEntry { get; }
        public string? CoverEntry { get; }

        public static MdmArchive Open(string path, MdmReadOptions? options = null)
        {
            var snapshot = (options ?? new MdmReadOptions()).Snapshot();
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (stream.Length > snapshot.MaxArchiveBytes)
                    throw new InvalidDataException("Archive exceeds compressed size limit.");
                return new MdmArchive(stream, snapshot);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public Stream OpenAsset(string name)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MdmArchive));
            if (!entries.TryGetValue(name, out var entry))
                throw new FileNotFoundException("Missing MDM asset.", name);
            return new BoundedReadStream(entry.Open(), entry.Length, crc32[name]);
        }

        public byte[] ReadAssetBytes(string name, int maxBytes)
        {
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            if (!entries.TryGetValue(name, out var entry))
                throw new FileNotFoundException("Missing MDM asset.", name);
            if (entry.Length > maxBytes)
                throw new InvalidDataException(name + " exceeds requested memory limit.");
            using (var stream = OpenAsset(name))
            using (var memory = new MemoryStream((int)entry.Length))
            {
                CopyBounded(stream, memory, maxBytes);
                return memory.ToArray();
            }
        }

        public void ExtractAsset(string name, string destination)
        {
            if (!entries.TryGetValue(name, out var entry))
                throw new FileNotFoundException("Missing MDM asset.", name);
            var created = false;
            try
            {
                using (var input = OpenAsset(name))
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    CopyBounded(input, output, Math.Min(options.MaxEntryBytes, entry.Length));
                }
            }
            catch
            {
                if (created)
                    File.Delete(destination);
                throw;
            }
        }

        private byte[] ReadPrefix(string name, int count)
        {
            using (var stream = OpenAsset(name))
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[count];
                int n;
                while (memory.Length < count && (n = stream.Read(buffer, 0, (int)(count - memory.Length))) > 0)
                    memory.Write(buffer, 0, n);
                return memory.ToArray();
            }
        }

        private void CheckAudioHeader(string name)
        {
            var head = ReadPrefix(name, 4);
            var ogg = head.Length >= 4 && Encoding.ASCII.GetString(head, 0, 4) == "OggS";
            var mp3 = head.Length >= 3 && ((head[0] == 'I' && head[1] == 'D' && head[2] == '3') || (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0));
            if (name.EndsWith(".ogg", StringComparison.Ordinal) ? !ogg : !mp3)
                throw new InvalidDataException(name + " has no recognized audio header.");
        }

        private static AlbumMetadata ParseMetadata(byte[] bytes)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("info.json must be UTF-8.", ex);
            }

            JObject obj;
            try
            {
                using (var stringReader = new StringReader(text))
                using (var reader = new JsonTextReader(stringReader)
                {
                    MaxDepth = 32,
                    DateParseHandling = DateParseHandling.None
                }
                )
                {
                    obj = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    while (reader.Read())
                        if (reader.TokenType != JsonToken.Comment)
                            throw new JsonReaderException("Trailing JSON content.");
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Invalid info.json: " + ex.Message, ex);
            }

            var values = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in obj.Properties())
            {
                if (values.ContainsKey(property.Name))
                    throw new InvalidDataException("Case-colliding metadata key: " + property.Name);
                values.Add(property.Name, property.Value);
            }

            string Read(string key, string fallback = "")
            {
                if (!values.TryGetValue(key, out var value) || value.Type == JTokenType.Null)
                    return fallback;
                if (value.Type != JTokenType.String && value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                    throw new InvalidDataException("Metadata " + key + " must be a string or number.");
                return value.Type == JTokenType.String ? value.Value<string>()! : value.ToString(Formatting.None);
            }

            var result = new AlbumMetadata
            {
                Name = Read("name"),
                NameRomanized = Read("name_romanized"),
                Author = Read("author"),
                LevelDesigner = Read("levelDesigner"),
                Bpm = Read("bpm", "0"),
                Scene = Read("scene", "scene_01"),
                HiddenDifficulty = Read("hideBmsDifficulty", "0"),
                HiddenMode = Read("hideBmsMode", "CLICK"),
                HiddenMessage = Read("hideBmsMessage"),
                RawJson = text
            };
            if (string.IsNullOrWhiteSpace(result.Name))
                throw new InvalidDataException("info.json must contain a nonempty name.");
            var difficulties = new Dictionary<int, string>();
            var designers = new Dictionary<int, string>();
            for (var i = 1; i <= 5; i++)
            {
                var value = Read("difficulty" + i, "0").Trim();
                if (value != "0" && value != "")
                    difficulties.Add(i, value);
                designers.Add(i, Read("levelDesigner" + i, result.LevelDesigner));
            }

            result.Difficulties = Collections.ReadOnly(difficulties);
            result.LevelDesigners = Collections.ReadOnly(designers);
            if (values.TryGetValue("searchTags", out var tags) && tags.Type != JTokenType.Null)
            {
                if (!(tags is JArray array) || array.Any(t => t.Type != JTokenType.String))
                    throw new InvalidDataException("searchTags must be a string array.");
                result.SearchTags = array.Select(t => t.Value<string>()!).ToList().AsReadOnly();
            }

            return result;
        }

        private static void ValidateEntryName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("/", StringComparison.Ordinal) || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0 || name.IndexOf('\0') >= 0)
                throw new InvalidDataException("Unsafe ZIP entry path.");
            var parts = name.TrimEnd('/').Split('/');
            foreach (var part in parts)
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal))
                    throw new InvalidDataException("Unsafe ZIP entry path: " + name);
        }

        private static List<uint> ValidateCentralDirectory(FileStream stream, MdmReadOptions options)
        {
            var tailLength = (int)Math.Min(stream.Length, 65557);
            if (tailLength < 22)
                throw new InvalidDataException("Truncated ZIP.");
            stream.Position = stream.Length - tailLength;
            var tail = new byte[tailLength];
            ReadExactly(stream, tail);
            var eocd = -1;
            for (var i = tailLength - 22; i >= 0; i--)
                if (BitConverter.ToUInt32(tail, i) == 0x06054b50 && i + 22 + BitConverter.ToUInt16(tail, i + 20) == tailLength)
                {
                    eocd = i;
                    break;
                }

            if (eocd < 0)
                throw new InvalidDataException("Missing ZIP end record.");
            var entriesOnDisk = BitConverter.ToUInt16(tail, eocd + 8);
            var count = BitConverter.ToUInt16(tail, eocd + 10);
            var size = BitConverter.ToUInt32(tail, eocd + 12);
            var offset = BitConverter.ToUInt32(tail, eocd + 16);
            if (BitConverter.ToUInt16(tail, eocd + 4) != 0 || BitConverter.ToUInt16(tail, eocd + 6) != 0 || entriesOnDisk != count)
                throw new InvalidDataException("Multi-disk ZIP is unsupported.");
            if (count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
                throw new InvalidDataException("ZIP64 is unnecessary for MDM archives within configured limits and is unsupported.");
            var endRecordOffset = stream.Length - tailLength + eocd;
            if (count > options.MaxEntries || (long)offset + size != endRecordOffset)
                throw new InvalidDataException("Invalid ZIP central directory bounds or entry count.");
            stream.Position = offset;
            var header = new byte[46];
            var checksums = new List<uint>(count);
            for (var i = 0; i < count; i++)
            {
                ReadExactly(stream, header);
                if (BitConverter.ToUInt32(header, 0) != 0x02014b50)
                    throw new InvalidDataException("Invalid ZIP central directory entry.");
                if ((BitConverter.ToUInt16(header, 8) & 1) != 0)
                    throw new InvalidDataException("Encrypted ZIP is unsupported.");
                var method = BitConverter.ToUInt16(header, 10);
                if (method != 0 && method != 8)
                    throw new InvalidDataException("ZIP compression must be Store or Deflate.");
                if (((BitConverter.ToUInt32(header, 38) >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("ZIP symbolic links are not accepted.");
                if (BitConverter.ToUInt16(header, 34) != 0)
                    throw new InvalidDataException("Multi-disk ZIP is unsupported.");
                checksums.Add(BitConverter.ToUInt32(header, 16));
                var skip = BitConverter.ToUInt16(header, 28) + BitConverter.ToUInt16(header, 30) + BitConverter.ToUInt16(header, 32);
                if (stream.Position + skip > (long)offset + size)
                    throw new InvalidDataException("Invalid ZIP entry bounds.");
                stream.Position += skip;
            }

            if (stream.Position != (long)offset + size)
                throw new InvalidDataException("Unexpected ZIP directory data.");
            return checksums;
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var n = stream.Read(buffer, offset, buffer.Length - offset);
                if (n == 0)
                    throw new InvalidDataException("Truncated ZIP directory.");
                offset += n;
            }
        }

        internal static void CopyBounded(Stream input, Stream output, long maxBytes)
        {
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (n > maxBytes - total)
                    throw new InvalidDataException("Stream exceeds size limit.");
                output.Write(buffer, 0, n);
                total += n;
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                zip.Dispose();
            }
        }

        private sealed class BoundedReadStream : Stream
        {
            private readonly Stream inner;
            private readonly long expected;
            private readonly uint expectedCrc;
            private uint crc = uint.MaxValue;
            private long read;
            public BoundedReadStream(Stream inner, long expected, uint expectedCrc)
            {
                this.inner = inner;
                this.expected = expected;
                this.expectedCrc = expectedCrc;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var n = inner.Read(buffer, offset, count);
                if (count == 0)
                    return n;
                if (n > expected - read)
                    throw new InvalidDataException("ZIP data exceeds declared length.");
                for (var i = offset; i < offset + n; i++)
                    crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                read += n;
                if (n == 0 && read != expected)
                    throw new InvalidDataException("Truncated ZIP entry.");
                if (read == expected && (crc ^ uint.MaxValue) != expectedCrc)
                    throw new InvalidDataException("ZIP entry CRC-32 mismatch.");
                return n;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    inner.Dispose();
                base.Dispose(disposing);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => expected;
            public override long Position { get => read; set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            private static readonly uint[] CrcTable = CreateCrcTable();
            private static uint[] CreateCrcTable()
            {
                var table = new uint[256];
                for (uint i = 0; i < table.Length; i++)
                {
                    var value = i;
                    for (var bit = 0; bit < 8; bit++)
                        value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
                    table[i] = value;
                }

                return table;
            }
        }
    }
}
