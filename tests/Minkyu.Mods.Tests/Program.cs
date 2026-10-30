using Minkyu.Mods;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
var root = Path.Combine(Path.GetTempPath(), "MinkyuMods-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var failed = 0;
var results = new List<object>();
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
void Test(string name, Action action)
{
    try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
try
{
    Test("Info+ rounding does not promote accuracy across a rank boundary", () =>
    {
        var counts = new InfoJudgements();
        counts.Miss(1, 1);
        var accuracy = counts.Accuracy(10001, 10000, 0, 0, 0, 0, false);
        Assert(accuracy < 100 && Math.Abs(accuracy - 99.99f) < .001f, "Near-perfect run rounded to 100%");
        Assert(counts.Accuracy(0, 0, 0, 0, 0, 0, true) == 100, "Empty chart produced invalid accuracy");
    });
    Test("Info+ areas retain the upstream default layout and can be configured separately", () =>
    {
        var path = Path.Combine(root, "fields.json");
        var settings = new ModSettings(path);
        Assert(settings.Get<string>("Info.Layout") == "Info+", "Simplified layout still defaults on");
        Assert(InfoFields.All.Length == 6, "Missing upstream text area");
        Assert(settings.Get<string>("Info.Field.UpperLeft.Text") == "<b>{overview}</b>", "Overview default differs");
        Assert(settings.Get<int>("Info.Field.UpperLeft.Size") == 60, "Overview size differs");
        Assert(!settings.Get<bool>("Info.Field.LowerRight.Enabled"), "Speed field should default off");
        settings.Set("Info.Field.UpperLeft.Enabled", false);
        settings.Set("Info.Field.ScoreRight.OffsetX", -80);
        settings = new ModSettings(path);
        Assert(!settings.Get<bool>("Info.Field.UpperLeft.Enabled") && settings.Get<bool>("Info.Field.ScoreRight.Enabled"), "Area switch changed another field");
        Assert(settings.Get<int>("Info.Field.ScoreRight.OffsetX") == -80, "Signed offset not retained");
    });
    Test("Info+ separates collectible misses, deduplicates notes and counts hold pairs", () =>
    {
        var counts = new InfoJudgements();
        counts.Miss(1, 1); counts.Miss(1, 1);
        counts.Miss(2, 6); counts.Miss(3, 7); counts.Miss(4, 4);
        counts.Miss(5, 3, true); counts.Miss(6, 3);
        counts.Block(7); counts.Block(7);
        Assert(counts.MissOther == 3 && counts.MissCollectible == 3 && counts.HoldPairs == 1, "Miss categories differ");
        Assert(counts.BlockCount == 1, "Block counted twice");
        counts.Miss(7, 2);
        Assert(counts.BlockCount == 0 && counts.MissOther == 4, "Missed block still counted as hit");
    });
    Test("Info+ offers descending and realtime accuracy without modifying game scores", () =>
    {
        var counts = new InfoJudgements();
        Assert(counts.Accuracy(100, 0, 0, 0, 0, 0, false) == 100, "New run is not 100%");
        Assert(Math.Abs(counts.Accuracy(100, 8, 2, 0, 0, 0, false) - 99) < .001, "Descending accuracy differs");
        Assert(Math.Abs(counts.Accuracy(100, 8, 2, 0, 0, 0, true) - 90) < .001, "Realtime accuracy differs");
        counts.Miss(1, 6);
        Assert(Math.Abs(counts.Accuracy(100, 8, 2, 0, 0, 0, false) - 98) < .001, "Collectible miss ignored");
    });
    Test("Info+ multihit misses wait for completion and are canceled by later hits", () =>
    {
        var counts = new InfoJudgements();
        counts.MultiMiss(1); counts.MultiMiss(1);
        Assert(counts.MissOther == 1, "Repeated callbacks counted twice");
        counts.MultiHit(1); counts.MultiMiss(1);
        Assert(counts.MissOther == 0, "Late multihit did not cancel its miss");
        counts.MultiHit(2); counts.MultiMiss(2);
        Assert(counts.MissOther == 0, "Successful multihit became a miss");
        counts.MultiMiss(3);
        Assert(counts.MissOther == 1, "Unplayed multihit was lost");
    });
    Test("each mod defaults off and has an independent switch", () =>
    {
        var path = Path.Combine(root, "independence.json");
        var settings = new ModSettings(path);
        var keys = new[] { "Info.Enabled", "Characters.Enabled", "Rank.Enabled", "Effects.Enabled", "Online.Enabled" };
        foreach (var key in keys) Assert(!settings.Get<bool>(key), key + " defaults on");
        foreach (var key in keys)
        {
            settings.Set(key, true);
            foreach (var other in keys) Assert(settings.Get<bool>(other) == (other == key), "Switch changed another mod");
            settings.Set(key, false);
        }
        Assert(!ModSettings.Definitions.Any(s => s.Key.Contains("Album", StringComparison.OrdinalIgnoreCase)), "Custom Albums must remain active");
    });
    foreach (var setting in ModSettings.Definitions)
        Test("persistent setting: " + setting.Key, () =>
        {
            var path = Path.Combine(root, "all-settings.json");
            var settings = new ModSettings(path);
            JToken value = setting.Default is bool b ? !b : setting.Default is int ? setting.Maximum : setting.Choices != null ? setting.Choices[^1] : "{acc} / {stats}";
            settings.Set(setting.Key, value);
            var reloaded = new ModSettings(path);
            if (setting.IsToggle) Assert(reloaded.Get<bool>(setting.Key) == value.Value<bool>(), "Toggle not retained");
            else if (setting.Default is int) Assert(reloaded.Get<int>(setting.Key) == value.Value<int>(), "Number not retained");
            else Assert(reloaded.Get<string>(setting.Key) == value.Value<string>(), "Text not retained");
            foreach (var key in new[] { "Info.Enabled", "Characters.Enabled", "Rank.Enabled", "Effects.Enabled", "Online.Enabled" })
                if (key != setting.Key) Assert(settings.Get<bool>(key) == new ModSettings(path).Get<bool>(key), "Persistence changed unrelated mod");
        });
    Test("invalid values clamp or return to defaults without type coercion", () =>
    {
        var settings = new ModSettings(Path.Combine(root, "invalid.json"));
        settings.Set("Info.Size", int.MaxValue);
        Assert(settings.Get<int>("Info.Size") == 48, "Upper bound not enforced");
        settings.Set("Characters.Size", 60);
        Assert(settings.Get<int>("Characters.Size") == 38, "Portraits can overlap Android leaderboard rows");
        settings.Set("Effects.JudgementSize", -123);
        Assert(settings.Get<int>("Effects.JudgementSize") == 0, "Lower bound not enforced");
        settings.Set("Info.Enabled", "true");
        Assert(!settings.Get<bool>("Info.Enabled"), "String coerced to toggle");
        settings.Set("Info.Position", "invalid");
        Assert(settings.Get<string>("Info.Position") == "Upper left", "Invalid choice accepted");
        settings.Set("Info.Format", new string('a', 513));
        Assert(settings.Get<string>("Info.Format") == "", "Unbounded text accepted");
    });
    Test("disabling retains configuration for the next enable", () =>
    {
        var path = Path.Combine(root, "off-on.json");
        var settings = new ModSettings(path);
        settings.Set("Info.Size", 42); settings.Set("Info.Enabled", true); settings.Set("Info.Enabled", false);
        settings = new ModSettings(path);
        Assert(settings.Get<int>("Info.Size") == 42 && !settings.Get<bool>("Info.Enabled"), "Disabled state lost data");
        settings.Set("Info.Enabled", true);
        Assert(settings.Get<int>("Info.Size") == 42, "Enable reset configuration");
    });
    Test("public leaderboard rejects malformed scores and bounds display names", () =>
    {
        var valid = "{code:0,result:[{play:{score:123,acc:99.5},user:{nickname:'<b>Player</b>'}}]}";
        var rows = LeaderboardResponse.Read(valid);
        Assert(rows.Count == 1 && !rows[0]["user"]["nickname"].ToString().Contains('<'), "Untrusted markup retained");
        foreach (var json in new[] { "{}", "{code:1,result:[]}", "{code:0,result:[{}]}",
                     "{code:0,result:[{play:{score:-1,acc:99},user:{}}]}",
                     "{code:0,result:[{play:{score:1,acc:101},user:{}}]}" })
        {
            var rejected = false;
            try { LeaderboardResponse.Read(json); } catch { rejected = true; }
            Assert(rejected, "Invalid server data accepted");
        }
        Assert(LeaderboardResponse.Read("{code:0,result:[]}").Count == 0, "Empty chart cannot be displayed");
    });
    Test("rank cache sorts, preserves tie policy and reports incomplete data", () =>
    {
        var cache = new RankCache();
        cache.Store("song_1", new[] { 80, 100, 90 });
        Assert(cache.Estimate("song_1", 90, false, false) == "#3", "Ties must follow existing scores");
        Assert(cache.Estimate("song_1", 101, true, true) == "#1!!!??", "Certainty/decoration differs");
        cache.Store("full_1", Enumerable.Range(1, 100));
        Assert(cache.Estimate("full_1", 0, true, false) == "#100+", "Outside leaderboard range misreported");
        cache.Store("999-0_1", new[] { 1 });
        Assert(cache.Estimate("999-0_1", 2, false, false) == "No ranking cached", "Official rank fabricated for custom song");
        cache.Store("empty", Array.Empty<int>());
        Assert(cache.Estimate("empty", 2, false, false) == "No ranking cached", "Empty scoreboard treated as known rank");
    });
    Test("rank cache is bounded and replaces an existing scoreboard", () =>
    {
        var cache = new RankCache();
        for (var i = 0; i < 9; i++) cache.Store("song" + i, new[] { 100 });
        Assert(cache.Estimate("song0", 200, false, false) == "No ranking cached", "Old scoreboard was not evicted");
        cache.Store("song8", new[] { 50 });
        Assert(cache.Estimate("song8", 75, false, false) == "#1", "Scoreboard not replaced");
    });
    Test("scoreboard parser handles missing companions and copied skills", () =>
    {
        var data = ScoreboardEntry.Parse(JObject.Parse("{play:{character_uid:'36',character_skill_uid:'0',score:100}}"));
        Assert(data.Character == 0 && data.Elfin == -1 && data.Score == 100, "Character/companion parsed incorrectly");
        Assert(ScoreboardEntry.Parse(JObject.Parse("{play:{character_uid:'invalid'}}")) == null, "Invalid character accepted");
        Assert(ScoreboardEntry.Parse(new JObject()) == null, "Missing data accepted");
        Assert(ScoreboardEntry.Parse(JObject.Parse("{detail:null,order:null}")) == null, "Anonymous self-rank was not handled");
    });
}
finally
{
    var report = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "mods-tests.json");
    Directory.CreateDirectory(Path.GetDirectoryName(report));
    File.WriteAllText(report, JsonConvert.SerializeObject(new { passed = results.Count - failed, failed, deviceTested = false, tests = results }, Formatting.Indented));
    Directory.Delete(root, true);
}
return failed == 0 ? 0 : 1;
