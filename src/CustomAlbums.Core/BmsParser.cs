using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CustomAlbums.Core
{
    public static class BmsParser
    {
        public static BmsChart Parse(byte[] bytes, int difficulty = 0, string entryName = "chart.bms", int maxEvents = 1000000)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (maxEvents <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEvents));
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(entryName + ": BMS must be UTF-8.", ex);
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var multipliers = new Dictionary<int, decimal>();
            var events = new List<BmsEvent>();
            var warnings = new List<string>();
            var lineNumber = 0;
            using (var reader = new StringReader(text))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    line = line.Trim();
                    if (!line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    line = line.Substring(1);
                    if (line.Length >= 6 && line[5] == ':' && IsDigits(line, 0, 3))
                    {
                        var measure = int.Parse(line.Substring(0, 3), CultureInfo.InvariantCulture);
                        var channel = line.Substring(3, 2).ToUpperInvariant();
                        var data = line.Substring(6).Trim();
                        if (channel == "02")
                        {
                            multipliers[measure] = PositiveDecimal(data, entryName + ": invalid measure multiplier at line " + lineNumber);
                            continue;
                        }

                        if (data.Length == 0)
                            continue;
                        if (data.Length % 2 != 0 || !IsBase36(data))
                            throw new InvalidDataException(entryName + ": invalid object pairs at line " + lineNumber);
                        var slots = data.Length / 2;
                        for (var slot = 0; slot < slots; slot++)
                        {
                            var value = data.Substring(slot * 2, 2).ToUpperInvariant();
                            if (value == "00")
                                continue;
                            if (events.Count == maxEvents)
                                throw new InvalidDataException(entryName + ": too many BMS events.");
                            events.Add(new BmsEvent { Measure = measure, Channel = channel, Value = value, Slot = slot, SlotsInMeasure = slots, SourceLine = lineNumber, Sequence = events.Count });
                        }
                    }
                    else
                    {
                        var split = 0;
                        while (split < line.Length && !char.IsWhiteSpace(line[split]))
                            split++;
                        if (split == line.Length)
                        {
                            warnings.Add("Ignored directive without value at line " + lineNumber + ".");
                            continue;
                        }

                        var key = line.Substring(0, split).ToUpperInvariant();
                        var value = line.Substring(split).Trim();
                        if (key == "BPM" && headers.ContainsKey(key))
                            throw new InvalidDataException(entryName + ": duplicate base BPM header.");
                        headers[key] = value;
                        if (key == "RANDOM" || key == "IF" || key == "ELSE" || key == "ENDIF")
                            throw new InvalidDataException(entryName + ": conditional/random BMS directives are unsupported.");
                    }
                }
            }

            if (!headers.TryGetValue("BPM", out var baseText))
                throw new InvalidDataException(entryName + ": missing #BPM.");
            var baseBpm = PositiveDecimal(baseText, entryName + ": invalid #BPM.");
            var bpmLookup = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
                if (header.Key.StartsWith("BPM", StringComparison.OrdinalIgnoreCase) && header.Key.Length == 5)
                    bpmLookup[header.Key.Substring(3)] = PositiveDecimal(header.Value, entryName + ": invalid " + header.Key);
            if (!headers.ContainsKey("GENRE"))
                warnings.Add("Missing #GENRE; stage scene must fall back to album metadata.");
            var unsupported = events.Select(e => e.Channel).Where(c => !KnownChannels.Contains(c)).Distinct().ToArray();
            foreach (var channel in unsupported)
                warnings.Add("Channel " + channel + " retained; no Muse Dash behavior is assigned.");
            if (unsupported.Contains("09"))
                throw new InvalidDataException(entryName + ": STOP channel 09 is unsupported; refusing inaccurate timing.");
            var ordered = events.OrderBy(e => e.Position).ThenBy(e => e.Channel == "03" || e.Channel == "08" ? 0 : e.Channel == "15" ? 1 : 2).ThenBy(e => e.Sequence).ToList();
            decimal position = 0, seconds = 0, bpm = baseBpm;
            foreach (var ev in ordered)
            {
                seconds += Advance(position, ev.Position, bpm, multipliers);
                ev.Seconds = seconds;
                position = ev.Position;
                if (ev.Channel == "03")
                {
                    if (!int.TryParse(ev.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var direct) || direct <= 0)
                        throw new InvalidDataException(entryName + ": invalid direct BPM at line " + ev.SourceLine);
                    bpm = direct;
                }
                else if (ev.Channel == "08")
                {
                    if (!bpmLookup.TryGetValue(ev.Value, out var lookup))
                        throw new InvalidDataException(entryName + ": missing #BPM" + ev.Value + " at line " + ev.SourceLine);
                    bpm = lookup;
                }
            }

            using (var md5 = MD5.Create())
                return new BmsChart
                {
                    Difficulty = difficulty,
                    EntryName = entryName,
                    BaseBpm = baseBpm,
                    Headers = Collections.ReadOnly(headers),
                    MeasureMultipliers = Collections.ReadOnly(multipliers),
                    Events = ordered.AsReadOnly(),
                    Warnings = warnings.AsReadOnly(),
                    RawBytes = (byte[])bytes.Clone(),
                    Md5 = Hex(md5.ComputeHash(bytes))
                };
        }

        private static decimal Advance(decimal from, decimal to, decimal bpm, Dictionary<int, decimal> multipliers)
        {
            decimal result = 0;
            while (from < to)
            {
                var measure = (int)decimal.Floor(from);
                var end = Math.Min(to, measure + 1m);
                var multiplier = multipliers.TryGetValue(measure, out var m) ? m : 1m;
                result += (end - from) * multiplier * 240m / bpm;
                from = end;
            }

            return result;
        }

        private static decimal PositiveDecimal(string value, string message)
        {
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || parsed > 1000000m)
                throw new InvalidDataException(message);
            return parsed;
        }

        private static bool IsDigits(string text, int start, int length)
        {
            for (var i = start; i < start + length; i++)
                if (text[i] < '0' || text[i] > '9')
                    return false;
            return true;
        }

        private static bool IsBase36(string text)
        {
            foreach (var c in text)
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
                    return false;
            return true;
        }

        internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static readonly HashSet<string> KnownChannels = new HashSet<string>(new[] { "01", "02", "03", "08", "15", "16", "18", "11", "13", "31", "33", "51", "53", "D1", "D3", "12", "14", "32", "34", "52", "54", "D2", "D4", "SC" });
    }
}
