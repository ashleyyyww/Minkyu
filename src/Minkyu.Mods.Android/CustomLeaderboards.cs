using HarmonyLib;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.UI.Panels;
using Newtonsoft.Json;
using UnityEngine.UI;
using UnityEngine.Networking;
using NativeArray = Il2CppNewtonsoft.Json.Linq.JArray;
using NativeObject = Il2CppNewtonsoft.Json.Linq.JObject;

namespace Minkyu.Mods;

internal static class CustomLeaderboards
{
    internal static bool MenuClosing { get; set; }
    private static readonly Dictionary<string, (DateTime At, string Json)> cache = new();
    private static PnlRank panel;
    private static string selectedUid, originalStatus;
    private static UnityWebRequest request;
    private static string requestedUid, requestedHash;
    private static bool? enabledState;

    internal static bool IsCustom(string uid) => uid?.StartsWith("999-", StringComparison.Ordinal) == true;

    internal static void Start(PnlRank target, string uid)
    {
        if (MenuClosing) return;
        if (panel != target)
        {
            RestoreStatus();
            originalStatus = target.repairing?.GetComponent<Text>()?.text;
        }
        panel = target;
        selectedUid = uid;
        enabledState = Mod.On("Online.Enabled");
        request?.Abort();
        request?.Dispose();
        request = null;
        if (!Mod.On("Online.Enabled")) { Status("Custom-map leaderboards are off"); return; }
        var separator = uid.LastIndexOf('_');
        if (separator < 0 || !int.TryParse(uid[(separator + 1)..], out var difficulty))
        {
            Status("Couldn't identify this chart");
            return;
        }
        var hash = CustomAlbums.Android.Mod.GetChartHash(uid[..separator], difficulty);
        if (hash == null || hash.Length != 32 || !hash.All(Uri.IsHexDigit))
        {
            Status("Couldn't identify this chart");
            return;
        }
        if (cache.TryGetValue(hash, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(2))
        {
            Apply(uid, cached.Json);
            return;
        }
        Status("Loading MDMC leaderboard...");
        // Use Unity networking: TLS fails in the bundled .NET HTTP stack.
        requestedUid = uid;
        requestedHash = hash;
        request = UnityWebRequest.Get("https://api.mdmc.moe/v3/sheets/" + hash + "/scores?limit=99&order=desc&page=1&format=md");
        request.timeout = 15;
        request.SetRequestHeader("User-Agent", "Minkyu/0.1");
        request.SendWebRequest();
    }

    internal static void Tick()
    {
        if (request == null) return;
        var stage = GlobalDataBase.dbBattleStage;
        var currentUid = stage?.selectedMusicInfo?.uid + "_" + stage?.selectedDifficulty;
        if (MenuClosing || panel == null || !panel.gameObject.activeInHierarchy || selectedUid != requestedUid || currentUid != requestedUid || !Mod.On("Online.Enabled"))
        {
            request.Abort();
            request.Dispose();
            request = null;
            return;
        }
        if (request.downloadedBytes > LeaderboardResponse.MaxBytes)
        {
            request.Abort();
            request.Dispose();
            request = null;
            Status("Leaderboard response was too large");
            return;
        }
        if (!request.isDone) return;
        var finished = request;
        request = null;
        try
        {
            if (finished.responseCode == 404) { Status("No online leaderboard for this chart"); return; }
            if (finished.isNetworkError || finished.isHttpError)
            {
                Mod.Log.Warning("MDMC leaderboard unavailable: " + finished.error);
                Status("Couldn't load the leaderboard. Try again.");
                return;
            }
            var rows = LeaderboardResponse.Read(finished.downloadHandler.text);
            var json = rows.ToString(Formatting.None);
            if (cache.Count >= 16) cache.Remove(cache.OrderBy(c => c.Value.At).First().Key);
            cache[requestedHash] = (DateTime.UtcNow, json);
            Apply(requestedUid, json);
        }
        catch (Exception ex)
        {
            Mod.Log.Warning("MDMC leaderboard rejected: " + ex.Message);
            Status("Couldn't read the leaderboard");
        }
        finally { finished.Dispose(); }
    }

    private static void Apply(string uid, string json)
    {
        var rows = NativeArray.Parse(json);
        panel.m_Ranks[uid] = rows;
        var separator = uid.LastIndexOf('_');
        var hash = CustomAlbums.Android.Mod.GetChartHash(uid[..separator], int.Parse(uid[(separator + 1)..]));
        RankPreview.Cache.Store("mdmc:" + hash, Newtonsoft.Json.Linq.JArray.Parse(json).Select(row => row["play"].Value<int>("score")));
        panel.m_SelfRank[uid] = NativeObject.Parse("{\"detail\":null,\"order\":null}");
        RestoreStatus();
        panel.UIRefresh(uid);
        panel.loading?.SetActive(false);
        panel.repairing?.SetActive(false);
        panel.noNet?.SetActive(false);
        panel.noRank?.SetActive(rows.Count == 0);
        panel.scrollView?.SetActive(rows.Count > 0);
        panel.server?.SetActive(false);
        Mod.Log.Msg("MDMC leaderboard loaded: " + rows.Count + " scores");
    }

    private static void Status(string message)
    {
        if (panel == null) return;
        if (panel.m_RankPool != null) panel.FastDestroyAll();
        panel.loading?.SetActive(false);
        panel.scrollView?.SetActive(false);
        panel.server?.SetActive(false);
        panel.noNet?.SetActive(false);
        panel.noRank?.SetActive(false);
        var text = panel.repairing?.GetComponent<Text>();
        if (text != null) text.text = message;
        panel.repairing?.SetActive(true);
    }

    internal static void Leave()
    {
        request?.Abort();
        request?.Dispose();
        request = null;
        selectedUid = null;
        RestoreStatus();
    }

    internal static void RestoreStatus()
    {
        if (panel != null && originalStatus != null)
        {
            var text = panel.repairing?.GetComponent<Text>();
            if (text != null) text.text = originalStatus;
        }
    }

    internal static void RefreshSettings()
    {
        var enabled = Mod.On("Online.Enabled");
        if (enabledState == enabled) return;
        enabledState = enabled;
        if (panel != null && panel.gameObject.activeInHierarchy && IsCustom(selectedUid)) Start(panel, selectedUid);
    }
}

[HarmonyPatch(typeof(Il2Cpp.PnlPreparation), nameof(Il2Cpp.PnlPreparation.OnBattleStart))]
internal static class RankingLeaveMenu
{
    private static void Prefix()
    {
        CustomLeaderboards.MenuClosing = true;
        CustomLeaderboards.Leave();
    }
}

[HarmonyPatch(typeof(Il2Cpp.PnlPreparation), nameof(Il2Cpp.PnlPreparation.OnEnable))]
internal static class RankingEnterMenu
{
    private static void Prefix() => CustomLeaderboards.MenuClosing = false;
}

[HarmonyPatch(typeof(PnlRank), nameof(PnlRank.RefreshGeneral))]
internal static class CustomRankingRequest
{
    private static bool Prefix(PnlRank __instance, string uid)
    {
        if (!CustomLeaderboards.IsCustom(uid)) { CustomLeaderboards.Leave(); return true; }
        Mod.Guard(() => CustomLeaderboards.Start(__instance, uid));
        return false;
    }
}

[HarmonyPatch(typeof(PnlRank), nameof(PnlRank.Refresh))]
internal static class CustomRankingRefresh
{
    private static bool Prefix(PnlRank __instance)
    {
        var stage = GlobalDataBase.dbBattleStage;
        var music = stage?.selectedMusicInfo?.uid;
        if (!CustomLeaderboards.IsCustom(music)) { CustomLeaderboards.Leave(); return true; }
        Mod.Guard(() => CustomLeaderboards.Start(__instance, music + "_" + stage.selectedDifficulty));
        return false;
    }
}
