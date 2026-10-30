using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Minkyu.Mods;

internal sealed record Setting(string Key, string Section, string Label, object Default, int Minimum = 0, int Maximum = 100, string[] Choices = null)
{
    internal bool IsToggle => Default is bool;
    internal bool IsText => Default is string && Choices == null;
    internal JToken Validate(JToken value)
    {
        if (Default is bool && value.Type == JTokenType.Boolean) return value;
        if (Default is int && value.Type == JTokenType.Integer)
        {
            var number = value.Value<long>();
            return new JValue(Math.Clamp(number, Minimum, Maximum));
        }
        if (Default is string && value.Type == JTokenType.String)
        {
            var text = value.Value<string>();
            if (text.Length <= 512 && (Choices == null || Choices.Contains(text))) return value;
        }
        return JToken.FromObject(Default);
    }
}

internal sealed class ModSettings
{
    internal static readonly Setting[] Definitions = new Setting[]
    {
        new("Info.Enabled", "Info+", "Enable Info+", false),
        new("Info.Chart", "Info+", "Song and chart info", true),
        new("Info.Accuracy", "Info+", "Accuracy and rank", true),
        new("Info.Judgements", "Info+", "Miss, Great, Early and Late", true),
        new("Info.Notes", "Info+", "Hit and total notes", true),
        new("Info.Best", "Info+", "Personal best and score gap", true),
        new("Info.Speed", "Info+", "Sky and ground speed", false),
        new("Info.Clock", "Info+", "Clock", false),
        new("Info.Layout", "Info+", "Layout", "Info+", Choices: new[] { "Info+", "Compact" }),
        new("Info.EarlyLate", "Info+", "Early and Late", "Only during AP", Choices: new[] { "Only during AP", "Always" }),
        new("Info.AccuracyMode", "Info+", "Accuracy", "From 100%", Choices: new[] { "From 100%", "Real-time" }),
        new("Info.BestCriteria", "Info+", "Personal best", "Highest accuracy", Choices: new[] { "Highest accuracy", "Highest score" }),
        new("Info.ReplaceResultMisses", "Info+", "Use Info+ misses on results", false),
        new("Info.Position", "Info+ display", "Position", "Upper left", Choices: new[] { "Upper left", "Upper right", "Lower left", "Lower right" }),
        new("Info.Size", "Info+ display", "Text size", 26, 16, 48),
        new("Info.Color", "Info+ display", "Text color", "White", Choices: new[] { "White", "Pink", "Yellow", "Cyan" }),
        new("Info.Outline", "Info+ display", "Text outline", true),
        new("Info.Format", "Info+ display", "Custom text", ""),
        new("Characters.Enabled", "Scoreboard Characters", "Enable Scoreboard Characters", false),
        new("Characters.Elfin", "Scoreboard Characters", "Show elfin", true),
        new("Characters.Self", "Scoreboard Characters", "Show your character", true),
        new("Characters.Equip", "Scoreboard Characters", "Tap to equip owned characters", false),
        new("Characters.Size", "Scoreboard Characters", "Icon size", 36, 24, 38),
        new("Rank.Enabled", "Rank Preview", "Enable Rank Preview", false),
        new("Rank.Certainty", "Rank Preview", "Mark incomplete rankings", true),
        new("Rank.Decoration", "Rank Preview", "Highlight high ranks", true),
        new("Rank.Size", "Rank Preview", "Text size", 60, 20, 80),
        new("Online.Enabled", "Leaderboards", "Custom-map leaderboards (MDMC)", false),
        new("Media.Dialogue", "Custom songs", "Chart dialogue", false),
        new("Media.Video", "Custom songs", "Background videos", false),
        new("Effects.Enabled", "SelectiveEffects", "Enable SelectiveEffects", false),
        new("Effects.All", "SelectiveEffects", "Hide battle effects", false),
        new("Effects.Fever", "Fever", "Hide all fever visuals", false),
        new("Effects.FeverBackground", "Fever", "Hide fever background", false),
        new("Effects.FeverStars", "Fever", "Hide fever stars", false),
        new("Effects.FeverTransition", "Fever", "Hide fever ending transition", false),
        new("Effects.Judgement", "Judgements", "Hide all judgements", false),
        new("Effects.Perfect", "Judgements", "Hide Perfect", false),
        new("Effects.TruePerfect", "Judgements", "Hide Perfect without Early or Late", false),
        new("Effects.Great", "Judgements", "Hide Great", false),
        new("Effects.Pass", "Judgements", "Hide Pass", false),
        new("Effects.JudgementSize", "Judgements", "Judgement size (%)", 100, 0, 100),
        new("Effects.HitAnimation", "Hit effects", "Hide enemy defeat animation", false),
        new("Effects.Hit", "Hit effects", "Hide hit effects", false),
        new("Effects.GirlHit", "Hit effects", "Hide character attack effects", false),
        new("Effects.Stars", "Hit effects", "Hide attack star particles", false),
        new("Effects.Hold", "Hit effects", "Hide hold effects", false),
        new("Effects.Music", "Other effects", "Hide music-note effects", false),
        new("Effects.Heart", "Other effects", "Hide heart effects", false),
        new("Effects.Boss", "Other effects", "Hide boss effects", false),
        new("Effects.Dust", "Other effects", "Hide landing dust", false),
        new("Effects.Hurt", "Other effects", "Hide damage text", false),
        new("Effects.Elfin", "Other effects", "Hide elfin effects", false),
        new("Effects.Background", "Stage", "Hide stage background", false),
        new("Effects.FloorOnly", "Stage", "Show only the floor", false),
        new("Effects.HitPoints", "Stage", "Hide hit-point circles", false),
        new("Effects.Girl", "Characters", "Hide character", false),
        new("Effects.HideElfin", "Characters", "Hide elfin", false),
        new("Effects.Health", "Interface", "Hide health and fever bars", false),
        new("Effects.Score", "Interface", "Hide score", false),
        new("Effects.Combo", "Interface", "Hide combo", false),
        new("Effects.Pause", "Interface", "Hide pause icon", false),
        new("Effects.Progress", "Interface", "Hide progress bar", false)
    }.Concat(InfoFields.Settings()).ToArray();

    private readonly string path;
    private JObject values = new();
    internal event Action Changed;
    internal ModSettings(string path)
    {
        this.path = path;
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Settings file is too large");
            using var input = File.OpenText(path);
            using var reader = new JsonTextReader(input) { MaxDepth = 4, DateParseHandling = DateParseHandling.None };
            var loaded = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new InvalidDataException("Unexpected content after settings");
            foreach (var setting in Definitions)
                if (loaded[setting.Key] is JToken value) values[setting.Key] = setting.Validate(value);
        }
    }
    internal T Get<T>(string key)
    {
        var setting = Definitions.Single(s => s.Key == key);
        return (values[key] ?? JToken.FromObject(setting.Default)).Value<T>();
    }
    internal void Set(string key, JToken value)
    {
        var setting = Definitions.Single(s => s.Key == key);
        var updated = (JObject)values.DeepClone();
        updated[key] = setting.Validate(value);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(directory);
        var pending = path + ".pending";
        try
        {
            using (var file = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(updated.ToString(Formatting.Indented));
                file.Write(bytes);
                file.Flush(true);
            }
            File.Move(pending, path, true);
            values = updated;
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
        Changed?.Invoke();
    }
}
