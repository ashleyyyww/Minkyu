using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomAlbums.Core
{
    public sealed class CinemaSettings
    {
        public string FileName { get; }
        public float Opacity { get; }
        public IReadOnlyList<int> Difficulties { get; }
        private CinemaSettings(string fileName, float opacity, int[] difficulties)
        {
            FileName = fileName;
            Opacity = opacity;
            Difficulties = Array.AsReadOnly(difficulties);
        }

        public static CinemaSettings? Read(MdmArchive archive)
        {
            if (!archive.Assets.Any(a => a.Name == "cinema.json")) return null;
            var json = ChartMedia.ReadObject(archive.ReadAssetBytes("cinema.json", 65536));
            var name = json.Value<string>("file_name");
            var opacity = json.Value<float?>("opacity") ?? 0;
            if (string.IsNullOrEmpty(name) || !name!.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                !archive.Assets.Any(a => a.Name == name) || float.IsNaN(opacity) || float.IsInfinity(opacity) || opacity < 0 || opacity > 1)
                throw new InvalidDataException("Invalid cinema.json video or opacity");
            var difficulties = json["difficulties"] == null ? new[] { 1, 2, 3, 4, 5 }
                : json["difficulties"] is JArray array && array.All(v => v.Type == JTokenType.Integer)
                    ? array.Values<int>().ToArray() : throw new InvalidDataException("Invalid cinema difficulties");
            if (difficulties.Length > 5 || difficulties.Any(v => v < 1 || v > 5))
                throw new InvalidDataException("Invalid cinema difficulties");
            return new CinemaSettings(name!, opacity, difficulties);
        }
    }

    public static class ChartMedia
    {
        internal static JObject ReadObject(byte[] bytes)
        {
            using (var text = new StringReader(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')))
            using (var reader = new JsonTextReader(text) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
            {
                var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new InvalidDataException("Unexpected media settings content");
                return result;
            }
        }

        public static JObject? ReadDialogue(MdmArchive archive, int difficulty)
        {
            var name = "map" + difficulty + ".talk";
            if (!archive.Assets.Any(a => a.Name == name)) return null;
            var data = ReadObject(archive.ReadAssetBytes(name, 4 * 1024 * 1024));
            var version = data.Value<int?>("version") ?? 1;
            if (version != 1 && version != 2) throw new InvalidDataException("Unsupported talk file version");
            foreach (var property in data.Properties().Where(p => p.Name != "version"))
            {
                if (property.Value is not JArray events || events.Count > 20000)
                    throw new InvalidDataException("Invalid dialogue events");
                foreach (var item in events)
                {
                    if (item is not JObject entry || entry.Value<string>("text")?.Length > 16384 ||
                        !decimal.TryParse(entry["time"]?.ToString(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var time) || time < 0 || time > 86400)
                        throw new InvalidDataException("Invalid dialogue event");
                }
            }
            return data;
        }
    }
}
