using Newtonsoft.Json.Linq;

namespace Minkyu.Mods;

internal sealed class RankCache
{
    private readonly Dictionary<string, int[]> scores = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();
    internal void Store(string key, IEnumerable<int> values)
    {
        if (string.IsNullOrEmpty(key) || key.StartsWith("999-", StringComparison.Ordinal)) return;
        var sorted = values.Where(s => s >= 0).OrderByDescending(s => s).Take(100).ToArray();
        if (!scores.ContainsKey(key)) order.Enqueue(key);
        scores[key] = sorted;
        while (order.Count > 8) scores.Remove(order.Dequeue());
    }
    internal string Estimate(string key, int score, bool certainty, bool decoration)
    {
        if (key == null || !scores.TryGetValue(key, out var board) || board.Length == 0) return "No ranking cached";
        var rank = board.TakeWhile(s => s >= score).Count() + 1;
        if (rank > 99) return "#100+";
        var mark = decoration ? rank <= 8 ? "!!!" : rank <= 20 ? "!!" : rank <= 50 ? "!" : "" : "";
        var unknown = certainty ? board.Length <= 50 ? "??" : board.Length <= 90 ? "?" : "" : "";
        return $"#{rank}{mark}{unknown}";
    }
}

internal sealed record ScoreboardEntry(int Character, int Elfin, int Score)
{
    internal static ScoreboardEntry Parse(JToken row)
    {
        if (row is not JObject) return null;
        var play = row["play"] as JObject ?? row["detail"] as JObject;
        if (play == null) return null;
        if (!int.TryParse(play.Value<string>("character_uid"), out var role) || role < 0) return null;
        // Phantom Thief Rin's copied skill comes from a separate API field.
        if (role == 36 && int.TryParse(play.Value<string>("character_skill_uid"), out var skill) && skill >= 0) role = skill;
        var elfin = int.TryParse(play.Value<string>("elfin_uid"), out var value) ? value : -1;
        var score = int.TryParse(play.Value<string>("score"), out var points) ? points : 0;
        return new(role, elfin, score);
    }
}
