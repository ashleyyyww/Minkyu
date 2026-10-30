using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.Helpers;
using Il2CppAssets.Scripts.PeroTools.Commons;
using Il2CppAssets.Scripts.PeroTools.Managers;
using Il2CppAssets.Scripts.PeroTools.Nice.Components;
using Il2CppAssets.Scripts.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using CharacterInfo = Il2CppAssets.Scripts.Database.CharacterInfo;

namespace Minkyu.Mods;

internal static class ScoreboardCharacters
{
    private static PnlRank lastPanel;
    private static string lastUid;
    private static Texture2D spritesheet;
    private static bool refreshPending;
    private static readonly Dictionary<(bool Character, int Index), Sprite> sprites = new();

    internal static Sprite Portrait(bool character, int index)
    {
        if (sprites.TryGetValue((character, index), out var sprite) && sprite != null) return sprite;
        if (spritesheet == null)
        {
            using var stream = typeof(ScoreboardCharacters).Assembly.GetManifestResourceStream("Minkyu.ScoreboardCharacters.png");
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            spritesheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(spritesheet, bytes.ToArray())) throw new InvalidDataException("Scoreboard sprites unavailable");
            spritesheet.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        var item = character ? index : index + 1;
        if (item < 0 || item >= (character ? 40 : 24)) return null;
        var row = item / (character ? 5 : 3);
        var column = character ? item % 5 : 5 + item % 3;
        var size = spritesheet.width / 8;
        sprite = Sprite.Create(spritesheet, new Rect(column * size, spritesheet.height - (row + 1) * size, size, size), new Vector2(.5f, .5f));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        sprites[(character, index)] = sprite;
        return sprite;
    }
    internal static void RefreshSettings()
    {
        refreshPending = true;
    }

    internal static void Tick()
    {
        if (CustomLeaderboards.MenuClosing || lastPanel == null || !lastPanel.isActiveAndEnabled) return;
        if (refreshPending)
        {
            Refresh(lastPanel, lastUid);
            refreshPending = false;
        }
        ScoreboardLoadout.EnsureVisible(lastPanel);
    }
    internal static void Refresh(PnlRank panel, string uid)
    {
        if (CustomLeaderboards.MenuClosing || panel == null || !panel.isActiveAndEnabled) return;
        lastPanel = panel;
        lastUid = uid;
        ScoreboardLoadout.Refresh(panel);
        var rows = new List<ScoreboardEntry>();
        if (uid != null && panel.m_Ranks.ContainsKey(uid))
        {
            var array = JArray.Parse(panel.m_Ranks[uid].ToString());
            rows.AddRange(array.Select(ScoreboardEntry.Parse));
            RankPreview.Cache.Store(uid, array.Select(r => (r?["play"] ?? r?["detail"])?["score"])
                .Where(v => v != null && int.TryParse(v.ToString(), out _)).Select(v => int.Parse(v.ToString())));
        }
        var self = uid != null && panel.m_SelfRank.ContainsKey(uid) ? ScoreboardEntry.Parse(JObject.Parse(panel.m_SelfRank[uid].ToString())) : null;
        if (panel.server != null) Fill(panel, panel.server, Mod.On("Characters.Self") ? self : null);
        if (panel.m_RankPool == null) return;
        var pool = panel.m_RankPool.gameObjects;
        for (var i = pool.Count - 1; i >= 0; i--)
        {
            var cell = pool[i];
            if (cell == null || !cell.activeInHierarchy) continue;
            var index = pool.Count - 1 - i;
            Fill(panel, cell, index < rows.Count ? rows[index] : null);
        }
    }
    private static void Fill(PnlRank panel, GameObject cell, ScoreboardEntry data)
    {
        var found = cell.transform.Find("Minkyu.Characters");
        if (!Mod.On("Characters.Enabled") || data == null)
        {
            if (found != null) found.gameObject.SetActive(false);
            return;
        }
        var holder = found?.gameObject;
        if (holder == null)
        {
            holder = new GameObject("Minkyu.Characters", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(), Il2CppInterop.Runtime.Il2CppType.Of<Button>());
            holder.transform.SetParent(cell.transform, false);
            CreateImage(holder.transform, "Character");
            CreateImage(holder.transform, "Elfin");
        }
        var rect = holder.GetComponent<RectTransform>();
        holder.SetActive(false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(200.5f, -.08f);
        var size = Mod.Settings.Get<int>("Characters.Size");
        rect.sizeDelta = new Vector2(size * 2, size);
        rect.localScale = Vector3.one;
        var configs = Singleton<ConfigManager>.instance;
        var role = configs.GetConfigObject<DBConfigCharacter>().GetCharacterInfoByIndex(data.Character);
        var elfin = data.Elfin < 0 ? null : configs.GetConfigObject<DBConfigElfin>().GetElfinInfoByIndex(data.Elfin);
        var image = holder.transform.Find("Character").GetComponent<Image>();
        var companion = holder.transform.Find("Elfin").GetComponent<Image>();
        image.sprite = Portrait(true, data.Character);
        if (image.sprite == null && role != null) image.sprite = PnlRank.GetSprite(role.GetSkin(0).GetHeadImgName());
        image.enabled = image.sprite != null;
        image.rectTransform.sizeDelta = new Vector2(size, size);
        image.rectTransform.anchoredPosition = new Vector2(-size / 2f, 0);
        companion.sprite = Portrait(false, data.Elfin);
        if (companion.sprite == null && elfin != null) companion.sprite = PnlRank.GetSprite(elfin.GetThumbImgName());
        companion.enabled = Mod.On("Characters.Elfin") && companion.sprite != null;
        companion.rectTransform.sizeDelta = new Vector2(size, size);
        companion.rectTransform.anchoredPosition = new Vector2(size / 2f, 0);
        var button = holder.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.interactable = Mod.On("Characters.Equip");
        button.onClick.AddListener((UnityAction)(Action)(() => Mod.Guard(() => Equip(panel, data, role, elfin))));
        holder.SetActive(true);
    }
    private static void CreateImage(Transform parent, string name)
    {
        var go = new GameObject(name, Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(), Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(), Il2CppInterop.Runtime.Il2CppType.Of<Image>());
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().preserveAspect = true;
    }
    private static void Equip(PnlRank panel, ScoreboardEntry data, CharacterInfo role, ElfinInfo elfin)
    {
        if (!Mod.On("Characters.Enabled") || !Mod.On("Characters.Equip") || role == null) return;
        if (!IAPHelper.CheckRoleIsUnlocked(role) || elfin != null && !GlobalDataBase.dbLevelConfig.IsDataUnlock(data.Elfin, "elfin")) return;
        GlobalDataBase.dbLevelConfig.curLevelConfigState = CurLevelConfigState.Default;
        DataHelper.selectedRoleIndex = data.Character;
        DataHelper.selectedElfinIndex = data.Elfin;
        GlobalDataBase.dbLevelConfig.ResetCurLevelConfig();
        panel.RefreshLevelConfigUI();
        // Database order starts at 1; menu scroll positions start at 0.
        var menus = panel.transform.root.Find("Standerd/PnlMenu/Panels");
        var roleScroll = menus?.Find("PnlRole")?.GetComponentInChildren<FancyScrollView>(true);
        var elfinScroll = menus?.Find("PnlElfin")?.GetComponentInChildren<FancyScrollView>(true);
        if (roleScroll != null) roleScroll.currentScrollPosition = role.order - 1;
        if (elfinScroll != null && elfin != null) elfinScroll.currentScrollPosition = elfin.order - 1;
    }
}

[HarmonyPatch(typeof(PnlRank), nameof(PnlRank.UIRefresh))]
internal static class RankingPatch
{
    private static void Postfix(PnlRank __instance, string uid) => Mod.Guard(() => ScoreboardCharacters.Refresh(__instance, uid));
}

[HarmonyPatch(typeof(PnlRank), nameof(PnlRank.RefreshLevelConfigUI))]
internal static class RankingLoadoutPatch
{
    private static void Postfix(PnlRank __instance) => Mod.Guard(() => ScoreboardLoadout.Refresh(__instance));
}
