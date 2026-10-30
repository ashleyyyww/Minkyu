using Newtonsoft.Json.Linq;

namespace Minkyu.Mods;

internal static class LeaderboardResponse
{
    internal const int MaxBytes = 2 * 1024 * 1024;

    internal static JArray Read(string json)
    {
        if (json.Length > MaxBytes) throw new InvalidDataException("Leaderboard response is too large");
        var root = JObject.Parse(json);
        if (root.Value<int?>("code") != 0 || root["result"] is not JArray rows || rows.Count > 99)
            throw new InvalidDataException("Invalid leaderboard response");
        var result = new JArray();
        foreach (var row in rows)
        {
            if (row["play"] is not JObject play || row["user"] is not JObject user)
                throw new InvalidDataException("Invalid leaderboard entry");
            var score = play.Value<long?>("score");
            var accuracy = play.Value<double?>("acc");
            if (score is null or < 0 or > int.MaxValue || accuracy == null || !double.IsFinite(accuracy.Value) || accuracy < 0 || accuracy > 100)
                throw new InvalidDataException("Invalid leaderboard score");
            var entry = (JObject)row.DeepClone();
            var nickname = user.Value<string>("nickname") ?? "Player";
            entry["user"]["nickname"] = new string(nickname.Take(80).Where(c => !char.IsControl(c) && c is not '<' and not '>').ToArray());
            result.Add(entry);
        }
        return result;
    }
}
