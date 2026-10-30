using System.Globalization;
using HarmonyLib;
using Il2Cpp;
using Il2CppFormulaBase;
using Il2CppAssets.Scripts.PeroTools.Nice.Interface;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore.HostComponent;
using Il2CppAssets.Scripts.GameCore.Managers;
using Il2CppAssets.Scripts.PeroTools.Commons;
using Il2CppAssets.Scripts.Helpers;
using Il2CppAssets.Scripts.UI.Panels;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Minkyu.Mods;

internal static class InfoOverlay
{
    private static Text label;
    private static PnlBattle panel;
    private static float nextRefresh;
    private static int total, sky, ground;
    private static string key, song, author, designer, bpm, difficulty;
    private static BestRun previous;
    private static Dictionary<string, BestRun> best = new();
    private static bool loaded;
    private static InfoJudgements judgements = new();
    private static string BestPath => Path.Combine(Application.persistentDataPath, "Minkyu", "personal-bests.json");
    private sealed record BestRun(int Score, float Accuracy, int Great, int Miss, int Early, int Late, int MissCollectible = 0, bool HasStats = true);

    internal static void Start(PnlBattle battle)
    {
        panel = battle;
        judgements = new();
        if (!Mod.On("Info.Enabled")) return;
        if (!loaded)
        {
            if (File.Exists(BestPath))
                best = JsonConvert.DeserializeObject<Dictionary<string, BestRun>>(File.ReadAllText(BestPath)) ?? new();
            loaded = true;
        }
        var stage = GlobalDataBase.dbBattleStage;
        var info = stage.selectedMusicInfo;
        var legacyKey = stage.musicUid + "_" + stage.selectedDifficulty;
        var hash = CustomAlbums.Android.Mod.GetChartHash(stage.musicUid, stage.selectedDifficulty);
        key = hash == null ? legacyKey : "mdm:" + hash;
        song = info?.name ?? "";
        author = info?.author ?? "";
        designer = info?.levelDesigner ?? "";
        bpm = info?.bpm ?? "";
        difficulty = stage.selectMusicLevelStr;
        previous = best.GetValueOrDefault(key) ?? best.GetValueOrDefault(legacyKey);
        var customBest = CustomAlbums.Android.Mod.GetPersonalBest(stage.musicUid, stage.selectedDifficulty);
        if (customBest != null)
            previous = previous == null ? new(customBest.Score, customBest.Accuracy, 0, 0, 0, 0, HasStats: false)
                : previous with { Score = customBest.Score, Accuracy = customBest.Accuracy };
        if (DataHelper.highest != null)
            foreach (var data in DataHelper.highest)
                if (data.fields.ContainsKey("uid") && data.fields.ContainsKey("score") && data.fields.ContainsKey("accuracy") && data.fields["uid"].GetResult<string>() == legacyKey)
                {
                    var score = data.fields["score"].GetResult<int>();
                    if (previous == null || score > previous.Score)
                        previous = new(score, data.fields["accuracy"].GetResult<float>() * 100f, 0, 0, 0, 0, HasStats: false);
                    break;
                }
        total = 0;
        sky = ground = 0;
        foreach (var note in StageBattleComponent.instance.GetMusicData())
        {
            if (note.noteData == null) continue;
            if (note.noteData.addCombo && !note.isLongPressing) total++;
            // Blocks, hearts and music notes count toward accuracy.
            if (note.noteData.type is 2 or 6 or 7) total++;
            if (note.noteData.type is < 1 or > 8 || note.isLongPressing) continue;
            if (note.isAir && sky == 0) sky = note.noteData.speed;
            if (!note.isAir && ground == 0) ground = note.noteData.speed;
        }
        if (label != null) Object.Destroy(label.gameObject);
        var source = panel.GetComponentInChildren<Text>(true);
        if (source == null) throw new InvalidOperationException("Native battle font unavailable");
        label = UiText.Create("Minkyu.InfoPlus", panel.transform, source);
        InfoHud.Start(panel);
        RefreshSettings();
        nextRefresh = 0;
    }

    internal static void RefreshSettings()
    {
        InfoHud.Refresh();
        if (label == null) return;
        label.gameObject.SetActive(Mod.On("Info.Enabled") && Mod.Settings.Get<string>("Info.Layout") == "Compact");
        label.fontSize = Mod.Settings.Get<int>("Info.Size");
        label.color = Mod.Settings.Get<string>("Info.Color") switch
        {
            "Pink" => new Color(1f, .45f, .75f), "Yellow" => new Color(1f, .93f, .35f),
            "Cyan" => new Color(.35f, .95f, 1f), _ => Color.white
        };
        var position = Mod.Settings.Get<string>("Info.Position");
        var right = position.EndsWith("right", StringComparison.Ordinal);
        var top = position.StartsWith("Upper", StringComparison.Ordinal);
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(right ? 1 : 0, top ? 1 : 0);
        rect.pivot = new Vector2(right ? 1 : 0, top ? 1 : 0);
        rect.anchoredPosition = new Vector2(right ? -35 : 35, top ? -185 : 65);
        rect.sizeDelta = new Vector2(720, 300);
        label.alignment = right ? (top ? TextAnchor.UpperRight : TextAnchor.LowerRight) : (top ? TextAnchor.UpperLeft : TextAnchor.LowerLeft);
        var outline = label.GetComponent<Outline>() ?? label.gameObject.AddComponent<Outline>();
        outline.enabled = Mod.On("Info.Outline");
        outline.effectColor = new Color(0, 0, 0, .85f);
        outline.effectDistance = new Vector2(1.3f, -1.3f);
    }

    internal static void Tick()
    {
        if (panel == null || key == null || !Mod.On("Info.Enabled") || !panel.gameObject.activeInHierarchy || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .1f;
        Mod.Guard(UpdateText);
    }
    private static void UpdateText()
    {
        var task = TaskStageTarget.instance;
        var role = BattleRoleAttributeComponent.instance;
        if (task == null || role == null) return;
        var separate = Mod.Settings.Get<string>("Info.Layout") == "Info+";
        var accuracy = separate ? judgements.Accuracy(total, task.m_PerfectResult, task.m_GreatResult, task.m_MusicCount, task.m_EnergyCount, task.m_RedPoint,
            Mod.Settings.Get<string>("Info.AccuracyMode") == "Real-time") : task.GetAccuracy() * 100f;
        var miss = separate ? judgements.MissOther + judgements.MissCollectible : task.GetMiss();
        var great = task.m_GreatResult;
        var rank = miss == 0 && great == 0 ? (role.early + role.late == 0 ? "TP" : "AP") : AccuracyRank(accuracy);
        var score = task.GetScore();
        var stats = $"{miss} Miss  {great} Great  {role.early} Early  {role.late} Late";
        var hit = separate ? task.m_PerfectResult + task.m_GreatResult + judgements.BlockCount + task.m_MusicCount + task.m_EnergyCount + task.m_RedPoint : task.GetHitCount();
        var values = new Dictionary<string, string>
        {
            ["song"] = song, ["author"] = author, ["levelDesigner"] = designer, ["bpm"] = bpm,
            ["diff"] = GlobalDataBase.dbBattleStage.selectedDifficulty.ToString(), ["level"] = difficulty,
            ["acc"] = accuracy.ToString("F2", CultureInfo.InvariantCulture) + "%", ["rank"] = rank,
            ["overview"] = rank is "TP" or "AP" ? rank : accuracy.ToString("F2", CultureInfo.InvariantCulture) + "%",
            ["stats"] = stats, ["hit"] = hit.ToString(), ["total"] = total.ToString(),
            ["pbScore"] = previous?.Score.ToString() ?? "—",
            ["pbAcc"] = previous == null ? "—" : previous.Accuracy.ToString("F2", CultureInfo.InvariantCulture) + "%",
            ["scoreGap"] = previous == null ? "—" : (score - previous.Score).ToString("+0;-0;0", CultureInfo.InvariantCulture),
            ["accGap"] = previous == null ? "—" : (accuracy - previous.Accuracy).ToString("+0.00;-0.00;0", CultureInfo.InvariantCulture) + "%",
            ["pbStats"] = previous?.HasStats != true ? "NO PB DATA" : $"{previous.Miss}M {previous.Great}G {previous.Early}E {previous.Late}L",
            ["pbGreat"] = previous?.HasStats == true ? previous.Great.ToString() : "—", ["pbMissOther"] = previous?.HasStats == true ? previous.Miss.ToString() : "—",
            ["pbMissCollectible"] = previous?.HasStats == true ? previous.MissCollectible.ToString() : "—", ["pbEarly"] = previous?.HasStats == true ? previous.Early.ToString() : "—", ["pbLate"] = previous?.HasStats == true ? previous.Late.ToString() : "—",
            ["pbStatsGap"] = previous?.HasStats != true ? "NO PB DATA" : $"{great - previous.Great:+0;-0;0}G {judgements.MissOther - previous.Miss:+0;-0;0}M {judgements.MissCollectible - previous.MissCollectible:+0;-0;0}H",
            ["skySpeed"] = sky.ToString(), ["groundSpeed"] = ground.ToString(), ["time"] = DateTime.Now.ToString("HH:mm")
        };
        if (Mod.Settings.Get<string>("Info.Layout") == "Info+")
        {
            string ColorText(string value, string color) => $"<color={color}>{value}</color>";
            var rankColor = rank switch { "TP" => "#ff0050", "AP" => "#fff000", "SS" => "#ccf0fe", "S" => "#ff0089", "A" => "#ad00ff", "B" => "#00bbff", "C" => "#00ff23", _ => "#a2a2a2" };
            values["overview"] = ColorText(rank is "AP" or "TP" ? rank : values["acc"], rankColor);
            values["acc"] = ColorText(values["acc"], rankColor);
            values["rank"] = ColorText(rank, rankColor);
            values["level"] = GlobalDataBase.dbBattleStage.selectedDifficulty switch { 1 => "EASY", 2 => "HARD", 3 => "MASTER", 4 => "HIDDEN", _ => "SPECIAL" };
            values["diff"] = difficulty;
            var parts = new List<string>();
            if (great > 0) parts.Add(great + "G");
            if (judgements.MissOther > 0) parts.Add(judgements.MissOther + "M");
            if (judgements.MissCollectible > 0) parts.Add(judgements.MissCollectible + "H");
            if (rank == "AP" || Mod.Settings.Get<string>("Info.EarlyLate") == "Always")
            {
                if (role.early > 0) parts.Add(ColorText(role.early + "E", "#38bfee"));
                if (role.late > 0) parts.Add(ColorText(role.late + "L", "#ff089f"));
            }
            values["stats"] = string.Join(" ", parts);
            values["scoreGap"] = previous == null ? "New" : score == previous.Score ? "" : ColorText(Math.Abs(score - previous.Score) < 1000
                ? (score - previous.Score).ToString("+0;-0", CultureInfo.InvariantCulture)
                : ((score - previous.Score) / 1000).ToString("+0;-0", CultureInfo.InvariantCulture) + "K", score >= previous.Score ? "#fe41f3" : "#9338fb");
            values["accGap"] = previous == null || Math.Abs(accuracy - previous.Accuracy) < .005f ? "" : ColorText(values["accGap"], accuracy >= previous.Accuracy ? "#fe41f3" : "#9338fb");
            InfoHud.Update(values);
            return;
        }
        var custom = Mod.Settings.Get<string>("Info.Format");
        if (custom.Length > 0)
        {
            foreach (var value in values) custom = custom.Replace("{" + value.Key + "}", value.Value, StringComparison.OrdinalIgnoreCase);
            label.text = custom.Replace("\\n", "\n");
            return;
        }
        var lines = new List<string>();
        if (Mod.On("Info.Chart")) lines.Add($"{song} · Lv.{difficulty}\n{author} · {bpm} BPM");
        if (Mod.On("Info.Accuracy")) lines.Add($"{values["acc"]}  {rank}");
        if (Mod.On("Info.Judgements")) lines.Add(stats);
        if (Mod.On("Info.Notes")) lines.Add($"{hit} / {total}");
        if (Mod.On("Info.Best")) lines.Add($"PB {values["pbScore"]}  {values["scoreGap"]}");
        if (Mod.On("Info.Speed")) lines.Add($"Sky {sky}  Ground {ground}");
        if (Mod.On("Info.Clock")) lines.Add(values["time"]);
        label.text = string.Join("\n", lines);
    }
    internal static void NoteResult(int index)
    {
        if (!Mod.On("Info.Enabled") || panel == null || key == null) return;
        var note = StageBattleComponent.instance?.GetMusicDataByIdx(index);
        if (note?.noteData == null || note.noteData.type is < 1 or > 8 || note.isLongPressing) return;
        if (note.isAir) sky = note.noteData.speed;
        else ground = note.noteData.speed;
    }

    internal static void PlayResult(int index, byte result)
    {
        if (key == null || !Mod.On("Info.Enabled")) return;
        var note = StageBattleComponent.instance.GetMusicDataByIdx(index);
        if (note?.noteData == null) return;
        if (result == 4 && note.noteData.type == 2) judgements.Block(note.objId);
        if (result == 1 && note.noteData.type == 3) judgements.Miss(note.objId, 3, note.isLongPressStart);
        if (note.noteData.type == 8) CountMulti(note, result);
        NoteResult(index);
    }

    internal static void MissNote(int index)
    {
        if (key == null || !Mod.On("Info.Enabled")) return;
        var result = BattleEnemyManager.instance.GetPlayResult(index);
        var stage = StageBattleComponent.instance;
        var note = stage.GetMusicDataByIdx(index);
        if (note?.noteData?.type == 8)
        {
            CountMulti(note, result);
            return;
        }
        if (result is not (0 or 1)) return;
        if (note?.noteData == null || note.noteData.type == 2 && result == 0) return;
        judgements.Miss(note.objId, note.noteData.type, note.isLongPressStart);
        if (note.isDouble && note.noteData.type is not (2 or 4 or 6 or 7 or 8))
        {
            var other = stage.GetMusicDataByIdx(note.doubleIdx);
            if (other != null) judgements.Miss(other.objId, other.noteData.type, other.isLongPressStart);
        }
        if (note.noteData.type != 2) NoteResult(index);
    }

    private static void CountMulti(Il2CppGameLogic.MusicData note, int result)
    {
        if (result is 3 or 4) judgements.MultiHit(note.objId);
        else if (result is 0 or 1)
            MelonLoader.MelonCoroutines.Start(DelayedMultiMiss(note.objId, (float)note.configData.length, judgements));
    }

    private static System.Collections.IEnumerator DelayedMultiMiss(int id, float delay, InfoJudgements run)
    {
        // A later hit within the multihit window cancels this miss.
        var stage = StageBattleComponent.instance;
        var tick = stage.realTimeTick;
        yield return new WaitForSeconds(delay);
        if (key != null && ReferenceEquals(judgements, run) && stage != null && stage.realTimeTick > tick)
            run.MultiMiss(id);
    }

    internal static string AccuracyRank(float acc) => acc >= 95 ? "SS" : acc >= 90 ? "S" : acc >= 80 ? "A" : acc >= 70 ? "B" : acc >= 60 ? "C" : "D";
    internal static void End(bool fail)
    {
        InfoHud.Clear();
        if (key == null) return;
        if (!fail && Mod.On("Info.Enabled"))
        {
            var task = TaskStageTarget.instance;
            var role = BattleRoleAttributeComponent.instance;
            var accuracy = task == null ? 0 : (float)Math.Round(task.GetAccuracy() * 100, 2);
            var improved = previous == null || (Mod.Settings.Get<string>("Info.BestCriteria") == "Highest score"
                ? task != null && task.GetScore() > previous.Score
                : task != null && accuracy > previous.Accuracy);
            if (task != null && role != null && improved)
            {
                best[key] = new(task.GetScore(), accuracy, task.m_GreatResult, judgements.MissOther, role.early, role.late, judgements.MissCollectible);
                var pending = BestPath + ".pending";
                Directory.CreateDirectory(Path.GetDirectoryName(BestPath));
                File.WriteAllText(pending, JsonConvert.SerializeObject(best));
                File.Move(pending, BestPath, true);
            }
        }
        if (label != null) Object.Destroy(label.gameObject);
        label = null;
        key = null;
    }

    internal static void Results(PnlVictory victory)
    {
        if (!Mod.On("Info.Enabled") || !Mod.On("Info.ReplaceResultMisses") || victory.m_CurControls?.missTxt == null) return;
        foreach (var text in victory.m_CurControls.missTxt)
            if (text != null) text.text = judgements.MissOther.ToString();
    }
}

internal static class UiText
{
    internal static Text Create(string name, Transform parent, Text source)
    {
        var go = new GameObject(name, Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(), Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(), Il2CppInterop.Runtime.Il2CppType.Of<Text>());
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = source.font;
        text.fontStyle = source.fontStyle;
        text.material = source.material;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}

[HarmonyPatch(typeof(PnlBattle), nameof(PnlBattle.GameStart))]
internal static class InfoBattleStart
{
    private static void Postfix(PnlBattle __instance) => Mod.Guard(() => InfoOverlay.Start(__instance));
}
[HarmonyPatch(typeof(StatisticsManager), nameof(StatisticsManager.OnBattleEnd))]
internal static class InfoBattleEnd
{
    private static void Prefix(bool fail) => Mod.Guard(() => InfoOverlay.End(fail));
}

[HarmonyPatch(typeof(BattleEnemyManager), nameof(BattleEnemyManager.SetPlayResult))]
internal static class InfoNoteSpeedPatch
{
    private static void Postfix(int idx, byte result) => Mod.Guard(() => InfoOverlay.PlayResult(idx, result));
}

[HarmonyPatch(typeof(Il2CppGameLogic.GameMissPlay), nameof(Il2CppGameLogic.GameMissPlay.MissCube))]
internal static class InfoMissPatch
{
    private static void Postfix(int idx) => Mod.Guard(() => InfoOverlay.MissNote(idx));
}

[HarmonyPatch(typeof(PnlVictory), nameof(PnlVictory.SetDetailInfo))]
internal static class InfoResultsPatch
{
    private static void Postfix(PnlVictory __instance) => Mod.Guard(() => InfoOverlay.Results(__instance));
}
