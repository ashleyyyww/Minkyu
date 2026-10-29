using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CustomAlbums.Core
{
    public sealed class MdmReadOptions
    {
        public long MaxArchiveBytes { get; set; } = 512L * 1024 * 1024;
        public long MaxTotalUncompressedBytes { get; set; } = 1024L * 1024 * 1024;
        public long MaxEntryBytes { get; set; } = 512L * 1024 * 1024;
        public int MaxEntries { get; set; } = 256;
        public int MaxChartBytes { get; set; } = 16 * 1024 * 1024;
        public int MaxMetadataBytes { get; set; } = 1024 * 1024;
        public int MaxEventsPerChart { get; set; } = 1000000;
        public int MaxCompressionRatio { get; set; } = 1000;
        public bool RequireRecognizedAudioHeader { get; set; } = true;

        internal MdmReadOptions Snapshot()
        {
            if (MaxArchiveBytes <= 0 || MaxTotalUncompressedBytes <= 0 || MaxEntryBytes <= 0 || MaxEntries <= 0 || MaxChartBytes <= 0 || MaxMetadataBytes <= 0 || MaxEventsPerChart <= 0 || MaxCompressionRatio <= 0)
                throw new ArgumentOutOfRangeException(nameof(MdmReadOptions), "All limits must be positive.");
            return (MdmReadOptions)MemberwiseClone();
        }
    }

    public sealed class AlbumMetadata
    {
        public string Name { get; internal set; } = "";
        public string NameRomanized { get; internal set; } = "";
        public string Author { get; internal set; } = "";
        public string LevelDesigner { get; internal set; } = "";
        public string Bpm { get; internal set; } = "0";
        public string Scene { get; internal set; } = "scene_01";
        public IReadOnlyDictionary<int, string> Difficulties { get; internal set; } = new Dictionary<int, string>();
        public IReadOnlyDictionary<int, string> LevelDesigners { get; internal set; } = new Dictionary<int, string>();
        public IReadOnlyList<string> SearchTags { get; internal set; } = new string[0];
        public string HiddenMode { get; internal set; } = "CLICK";
        public string HiddenDifficulty { get; internal set; } = "0";
        public string HiddenMessage { get; internal set; } = "";
        public string RawJson { get; internal set; } = "";
    }

    public sealed class BmsEvent
    {
        public int Measure { get; internal set; }
        public int Slot { get; internal set; }
        public int SlotsInMeasure { get; internal set; }
        public string Channel { get; internal set; } = "";
        public string Value { get; internal set; } = "";
        public decimal Seconds { get; internal set; }
        public int SourceLine { get; internal set; }
        internal decimal Position => Measure + (decimal)Slot / SlotsInMeasure;
        internal int Sequence { get; set; }
    }

    public sealed class BmsChart
    {
        public int Difficulty { get; internal set; }
        public string EntryName { get; internal set; } = "";
        public decimal BaseBpm { get; internal set; }
        public IReadOnlyDictionary<string, string> Headers { get; internal set; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<int, decimal> MeasureMultipliers { get; internal set; } = new Dictionary<int, decimal>();
        public IReadOnlyList<BmsEvent> Events { get; internal set; } = new BmsEvent[0];
        public IReadOnlyList<string> Warnings { get; internal set; } = new string[0];
        public byte[] RawBytes { get; internal set; } = new byte[0];
        public string Md5 { get; internal set; } = "";
    }

    public sealed class MdmAsset
    {
        public string Name { get; internal set; } = "";
        public long Length { get; internal set; }
        public long CompressedLength { get; internal set; }
    }

    internal static class Collections
    {
        internal static IReadOnlyDictionary<TKey, TValue> ReadOnly<TKey, TValue>(Dictionary<TKey, TValue> value) => new ReadOnlyDictionary<TKey, TValue>(value);
    }
}
