using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore.HostComponent;
using Il2CppAssets.Scripts.UI.Panels;
using UnityEngine;
using UnityEngine.UI;
using MelonLoader;
using System.Collections;

namespace Minkyu.Mods;

internal static class RankPreview
{
    internal static readonly RankCache Cache = new();
    private static Text label;
    private static int animationGeneration;
    internal static void Show(PnlVictory victory)
    {
        if (label != null) label.gameObject.SetActive(false);
        if (!Mod.On("Rank.Enabled")) return;
        var stage = GlobalDataBase.dbBattleStage;
        var custom = stage.musicUid?.StartsWith("999-", StringComparison.Ordinal) == true;
        if (custom && !Mod.On("Online.Enabled")) return;
        var key = custom
            ? "mdmc:" + CustomAlbums.Android.Mod.GetChartHash(stage.musicUid, stage.selectedDifficulty)
            : stage.musicUid + "_" + stage.selectedDifficulty;
        var source = victory.m_CurControls?.scoreTxt;
        if (source == null) return;
        var title = source.transform.parent.GetComponent<Text>();
        if (title == null) return;
        var existing = title.transform.parent.Find("Minkyu.RankPreview");
        label = existing != null ? existing.GetComponent<Text>() : UiText.Create("Minkyu.RankPreview", title.transform.parent, title);
        label.font = source.font;
        label.fontSize = Mod.Settings.Get<int>("Rank.Size");
        label.color = title.color;
        label.alignment = title.alignment;
        label.rectTransform.anchorMin = title.rectTransform.anchorMin;
        label.rectTransform.anchorMax = title.rectTransform.anchorMax;
        label.rectTransform.pivot = title.rectTransform.pivot;
        label.rectTransform.sizeDelta = title.rectTransform.sizeDelta;
        label.rectTransform.localScale = title.rectTransform.localScale;
        label.rectTransform.localRotation = title.rectTransform.localRotation;
        label.rectTransform.position = title.rectTransform.position + new Vector3(-4, 8, 0);
        var position = label.rectTransform.anchoredPosition;
        label.rectTransform.anchoredPosition = new Vector2(position.x, 230);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.text = Cache.Estimate(key, TaskStageTarget.instance.GetScore(), Mod.On("Rank.Certainty"), Mod.On("Rank.Decoration"));
        label.gameObject.SetActive(true);
        MelonCoroutines.Start(Animate(label, ++animationGeneration));
    }

    private static IEnumerator Animate(Text target, int generation)
    {
        var group = target.GetComponent<CanvasGroup>() ?? target.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        group.alpha = 0;
        var endPosition = target.rectTransform.anchoredPosition;
        var started = Time.unscaledTime;
        while (target != null && generation == animationGeneration && Time.unscaledTime - started < 1.25f) yield return null;
        while (target != null && generation == animationGeneration)
        {
            var progress = Mathf.Clamp01((Time.unscaledTime - started - 1.25f) * 3);
            group.alpha = progress;
            target.rectTransform.anchoredPosition = endPosition - new Vector2(0, 30 * (1 - progress));
            if (progress >= 1) break;
            yield return null;
        }
    }
}

[HarmonyPatch(typeof(PnlVictory), nameof(PnlVictory.SetScore))]
internal static class RankVictoryPatch
{
    private static void Postfix(PnlVictory __instance) => Mod.Guard(() => RankPreview.Show(__instance));
}
