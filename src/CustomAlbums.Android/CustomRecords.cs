using CustomAlbums.Core;
using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore.HostComponent;
using Il2CppAssets.Scripts.GameCore.Managers;
using Il2CppAssets.Scripts.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CustomAlbums.Android;

internal static class CustomRecords
{
    private static ChartRecords records;
    private static string playingHash;
    private static ChartRecord previous;
    private static bool saved;
    internal static bool Completed { get; set; }

    internal static void Initialize()
    {
        records = new ChartRecords(Path.Combine(AlbumCatalog.Root, "chart-records.json"));
        var oldPath = Path.Combine(Application.persistentDataPath, "Minkyu", "personal-bests.json");
        if (!File.Exists(oldPath)) return;
        var old = JObject.Parse(File.ReadAllText(oldPath));
        foreach (var album in AlbumCatalog.Albums)
            foreach (var chart in album.Charts)
            {
                var hash = chart.Value.Md5;
                if (records.Get(hash) != null) continue;
                var value = old["mdm:" + hash] ?? old[album.Uid + "_" + chart.Key];
                if (value != null) records.ImportSummary(hash, value.Value<int>("Score"), value.Value<float>("Accuracy"));
            }
    }

    internal static ChartRecord Get(string uid, int difficulty)
    {
        var hash = Mod.GetChartHash(uid, difficulty);
        return hash == null ? null : records?.Get(hash);
    }

    internal static void Begin()
    {
        var stage = GlobalDataBase.dbBattleStage;
        playingHash = Mod.GetChartHash(stage.musicUid, stage.selectedDifficulty);
        previous = playingHash == null ? null : records?.Get(playingHash);
        saved = false;
        Completed = false;
    }

    internal static void Result(PnlVictory victory)
    {
        var stage = GlobalDataBase.dbBattleStage;
        var hash = Mod.GetChartHash(stage.musicUid, stage.selectedDifficulty);
        if (hash == null || records == null) return;
        if (playingHash != hash) throw new InvalidOperationException("Custom chart changed during gameplay");
        var task = TaskStageTarget.instance;
        var controls = victory.m_CurControls;
        if (task == null || controls == null) return;
        if (Completed && !saved)
        {
            records.RecordClear(hash, task.GetScore(), task.GetAccuracy() * 100, task.GetComboMax(), task.GetStageEvaluate().Value, task.IsFullCombo());
            saved = true;
        }
        controls.highScoreTitle.enabled = previous != null;
        controls.highScoreTxt.enabled = previous != null;
        controls.highScoreTxt.text = previous?.Score.ToString() ?? "-";
        controls.newBest.SetActive(Completed && (previous == null || task.GetScore() > previous.Score));
    }

    internal static bool Preparation(PnlRecord panel)
    {
        var stage = GlobalDataBase.dbBattleStage;
        var info = GlobalDataBase.dbMusicTag.CurMusicInfo();
        if (info == null || !CustomDataIsolation.IsCustom(info.uid)) return true;
        panel.Clear();
        var record = Get(info.uid, stage.selectedDifficulty);
        if (record == null) return false;
        panel.txtScore.text = record.Score.ToString();
        panel.txtAccuracy.text = record.Accuracy.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "%";
        panel.txtCombo.text = record.Combo?.ToString() ?? "-";
        panel.txtClear.text = record.Clears == 0 ? "-" : record.Clears.ToString();
        var evaluate = record.Evaluate ?? (record.Accuracy >= 95 ? 5 : record.Accuracy >= 90 ? 4 : record.Accuracy >= 80 ? 3 : record.Accuracy >= 70 ? 2 : record.Accuracy >= 60 ? 1 : 0);
        panel.txtGrade.text = new[] { "D", "C", "B", "A", "S", "S", "S" }[evaluate];
        panel.txtGrade.color = panel.gradeColor[evaluate];
        panel.imgIconFc.SetActive(record.FullCombo);
        if (record.FullCombo) panel.txtCombo.color = panel.fullComboColor;
        return false;
    }

    internal static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Mod.Log.Error("CA_RECORD_FAILED " + ex); }
    }
}

[HarmonyPatch(typeof(StatisticsManager), nameof(StatisticsManager.OnBattleEnd))]
internal static class CustomRecordEnd
{
    private static void Prefix(bool fail) => CustomRecords.Completed = !fail;
}

[HarmonyPatch(typeof(PnlBattle), nameof(PnlBattle.GameStart))]
internal static class CustomRecordStart
{
    private static void Prefix() => CustomRecords.Guard(CustomRecords.Begin);
}

[HarmonyPatch(typeof(PnlVictory), nameof(PnlVictory.SetScore))]
internal static class CustomRecordResult
{
    private static void Postfix(PnlVictory __instance) => CustomRecords.Guard(() => CustomRecords.Result(__instance));
}

[HarmonyPatch(typeof(PnlRecord), nameof(PnlRecord.RefreshRecord))]
internal static class CustomRecordPreparation
{
    private static bool Prefix(PnlRecord __instance)
    {
        var runOriginal = true;
        CustomRecords.Guard(() => runOriginal = CustomRecords.Preparation(__instance));
        return runOriginal;
    }
}
