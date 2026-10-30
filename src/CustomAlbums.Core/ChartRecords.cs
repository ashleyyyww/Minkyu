using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace CustomAlbums.Core
{
    public sealed class ChartRecord
    {
        public int Score { get; set; }
        public float Accuracy { get; set; }
        public int? Combo { get; set; }
        public int? Evaluate { get; set; }
        public int Clears { get; set; }
        public bool FullCombo { get; set; }
    }

    public sealed class ChartRecords
    {
        private readonly string path;
        private Dictionary<string, ChartRecord> records;

        public ChartRecords(string path)
        {
            this.path = Path.GetFullPath(path);
            if (File.Exists(path) && new FileInfo(path).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Chart records file is too large");
            records = File.Exists(path)
                ? JsonConvert.DeserializeObject<Dictionary<string, ChartRecord>>(File.ReadAllText(path))
                    ?? throw new InvalidDataException("Chart records file is empty")
                : new Dictionary<string, ChartRecord>(StringComparer.Ordinal);
            foreach (var pair in records)
            {
                Validate(pair.Key, pair.Value.Score, pair.Value.Accuracy);
                if (pair.Value.Combo < 0 || pair.Value.Evaluate < 0 || pair.Value.Evaluate > 6 || pair.Value.Clears < 0)
                    throw new InvalidDataException("Invalid chart record");
            }
        }

        public ChartRecord? Get(string hash)
        {
            Validate(hash, 0, 0);
            if (!records.TryGetValue(hash.ToLowerInvariant(), out var record)) return null;
            return Copy(record);
        }

        public void ImportSummary(string hash, int score, float accuracy)
            => Save(hash, score, accuracy, null, null, false, false);

        public void RecordClear(string hash, int score, float accuracy, int combo, int evaluate, bool fullCombo)
        {
            if (combo < 0 || evaluate < 0 || evaluate > 6) throw new InvalidDataException("Invalid clear result");
            Save(hash, score, accuracy, combo, evaluate, fullCombo, true);
        }

        private void Save(string hash, int score, float accuracy, int? combo, int? evaluate, bool fullCombo, bool cleared)
        {
            Validate(hash, score, accuracy);
            hash = hash.ToLowerInvariant();
            var updated = new Dictionary<string, ChartRecord>(records, StringComparer.Ordinal);
            var record = Get(hash) ?? new ChartRecord();
            record.Score = Math.Max(record.Score, score);
            record.Accuracy = Math.Max(record.Accuracy, accuracy);
            if (combo.HasValue) record.Combo = Math.Max(record.Combo ?? 0, combo.Value);
            if (evaluate.HasValue) record.Evaluate = Math.Max(record.Evaluate ?? 0, evaluate.Value);
            record.FullCombo |= fullCombo;
            if (cleared) record.Clears = checked(record.Clears + 1);
            updated[hash] = record;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var pending = path + ".pending";
            try
            {
                using (var file = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(updated, Formatting.Indented));
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
                records = updated;
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }

        private static void Validate(string hash, int score, float accuracy)
        {
            if (hash == null || hash.Length != 32 || !hash.All(Uri.IsHexDigit) || score < 0 ||
                float.IsNaN(accuracy) || float.IsInfinity(accuracy) || accuracy < 0 || accuracy > 100)
                throw new InvalidDataException("Invalid chart record");
        }

        private static ChartRecord Copy(ChartRecord value) => new ChartRecord
        {
            Score = value.Score, Accuracy = value.Accuracy, Combo = value.Combo,
            Evaluate = value.Evaluate, Clears = value.Clears, FullCombo = value.FullCombo
        };
    }
}
