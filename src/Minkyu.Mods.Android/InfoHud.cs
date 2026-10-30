using System.Collections;
using MelonLoader;
using Il2CppAssets.Scripts.UI.Panels;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Minkyu.Mods;

internal static class InfoHud
{
    private static readonly Dictionary<string, Font> fonts = new();
    private static readonly Dictionary<string, Text> labels = new();
    private static GameObject root;
    private static PnlBattle battle;
    private static bool loadingFonts;
    private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> corners = new(4L);

    internal static void LoadFonts()
    {
        if (loadingFonts) return;
        loadingFonts = true;
        MelonCoroutines.Start(ReadFonts());
    }

    private static IEnumerator ReadFonts()
    {
        foreach (var (name, address) in new[] { ("Snaps Taste", "Snaps Taste"), ("Normal", "Normal"), ("Lato", "Lato-Regular"), ("Luckiest Guy", "LuckiestGuy-Regular") })
        {
            var handle = Addressables.LoadAssetAsync<Font>(address);
            // A synchronous Addressables wait can deadlock during scene loads.
            while (!handle.IsDone) yield return null;
            if (handle.Result != null) fonts[name] = handle.Result;
            else Mod.Log.Warning("Info+ font unavailable: " + name);
        }
        Refresh();
    }

    internal static void Start(PnlBattle panel)
    {
        Clear();
        battle = panel;
        var source = panel.CurrentBattleUIComp?.scoreValue?.text ?? panel.GetComponentInChildren<Text>(true);
        if (source == null) throw new InvalidOperationException("Native battle text unavailable");
        root = new GameObject("Minkyu.InfoPlus", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        root.transform.SetParent(panel.transform, false);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        foreach (var field in InfoFields.All)
            labels[field.Id] = UiText.Create("InfoPlus_Text" + field.Id, root.transform, source);
        LoadFonts();
        Refresh();
    }

    internal static void Refresh()
    {
        if (root == null || battle == null) return;
        root.SetActive(Mod.On("Info.Enabled") && Mod.Settings.Get<string>("Info.Layout") == "Info+");
        foreach (var field in InfoFields.All)
        {
            var text = labels[field.Id];
            var key = "Info.Field." + field.Id + ".";
            text.gameObject.SetActive(Mod.On(key + "Enabled"));
            if (fonts.TryGetValue(Mod.Settings.Get<string>(key + "Font"), out var font) && font != null) text.font = font;
            text.material = null;
            text.fontSize = Mod.Settings.Get<int>(key + "Size");
            text.color = ColorFor(Mod.Settings.Get<string>(key + "Color"));
            text.fontStyle = field.Id.StartsWith("Lower", StringComparison.Ordinal) ? FontStyle.Italic : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = field.Id == "UpperRight" ? .8f : 1;
            var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
            outline.enabled = Mod.On(key + "Outline");
            outline.effectColor = ColorFor(Mod.Settings.Get<string>(key + "OutlineColor"));
            var width = Mod.Settings.Get<int>(key + "OutlineWidth");
            outline.effectDistance = new Vector2(width, width);
        }
        Position();
    }

    internal static void Position()
    {
        if (root == null || battle == null || !root.activeInHierarchy) return;
        var comp = battle.CurrentBattleUIComp;
        var score = comp?.scoreValue?.GetComponentsInChildren<Text>(true)
            .FirstOrDefault(t => t.gameObject.activeInHierarchy && !t.transform.IsChildOf(root.transform));
        var compRoot = comp?.scoreValue?.transform;
        while (compRoot != null && compRoot.Find("Score") == null && compRoot != battle.transform) compRoot = compRoot.parent;
        var scoreRoot = compRoot?.Find("Score");
        var title = scoreRoot?.Find("Other/ScoreTittle");
        if (title != null)
        {
            var graphic = title.GetComponentsInChildren<Graphic>(false).FirstOrDefault(g => g.gameObject.activeInHierarchy);
            if (graphic != null) title = graphic.transform;
        }
        if (title == null || !title.gameObject.activeInHierarchy) title = score?.transform;
        var scoreBounds = Bounds(score?.rectTransform);
        var titleBounds = Bounds(title?.TryCast<RectTransform>());
        Place("ScoreRight", new Vector2(0, 1), new Vector2(0, .5f), scoreBounds.Right + new Vector2(18, score != null && score.rectTransform.rect.height < 1 ? -score.fontSize * .5f : 0), TextAnchor.MiddleLeft, true);
        Place("ScoreBelow", new Vector2(0, 1), new Vector2(0, .5f), titleBounds.Right + new Vector2(18, 0), TextAnchor.MiddleLeft, true);
        Place("UpperLeft", new Vector2(0, 1), new Vector2(0, 1), titleBounds.BottomLeft + new Vector2(0, -50), TextAnchor.UpperLeft, true);
        var pause = compRoot?.Find("Up/BtnPause")?.TryCast<RectTransform>();
        var topRight = pause == null ? new Vector2(-120, -25) : Bounds(pause).Left + new Vector2(-25, 0);
        Place("UpperRight", new Vector2(1, 1), new Vector2(1, 1), topRight, TextAnchor.UpperRight, pause != null);
        Place("LowerLeft", Vector2.zero, Vector2.zero, new Vector2(35, 45), TextAnchor.LowerLeft, false);
        Place("LowerRight", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-35, 45), TextAnchor.LowerRight, false);
    }

    private static (Vector2 Left, Vector2 Right, Vector2 BottomLeft) Bounds(RectTransform rect)
    {
        if (rect == null) return (new Vector2(-1000, 400), new Vector2(-750, 400), new Vector2(-1000, 370));
        // Use a long length: the int overload treats 4 as a native pointer.
        rect.GetWorldCorners(corners);
        var bottomLeft = root.transform.InverseTransformPoint(corners[0]);
        var topLeft = root.transform.InverseTransformPoint(corners[1]);
        var topRight = root.transform.InverseTransformPoint(corners[2]);
        return (new Vector2(bottomLeft.x, (bottomLeft.y + topLeft.y) / 2), new Vector2(topRight.x, (bottomLeft.y + topLeft.y) / 2), new Vector2(bottomLeft.x, bottomLeft.y));
    }

    private static void Place(string id, Vector2 anchor, Vector2 pivot, Vector2 position, TextAnchor alignment, bool local)
    {
        var text = labels[id];
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = local ? new Vector2(.5f, .5f) : anchor;
        rect.pivot = pivot;
        rect.sizeDelta = new Vector2(id == "UpperRight" ? 600 : 900, 160);
        var key = "Info.Field." + id + ".";
        rect.anchoredPosition = position + new Vector2(Mod.Settings.Get<int>(key + "OffsetX"), Mod.Settings.Get<int>(key + "OffsetY"));
        rect.localScale = Vector3.one;
        text.alignment = alignment;
    }

    internal static void Update(Dictionary<string, string> values)
    {
        if (root == null || !root.activeInHierarchy) return;
        Position();
        foreach (var field in InfoFields.All)
        {
            var text = Mod.Settings.Get<string>("Info.Field." + field.Id + ".Text");
            foreach (var value in values) text = text.Replace("{" + value.Key + "}", value.Value, StringComparison.OrdinalIgnoreCase);
            labels[field.Id].text = text.Replace("\\n", "\n").Trim().Trim('/').Trim();
        }
    }

    internal static Color ColorFor(string name) => name switch
    {
        "Black" => Color.black, "Pink" => new Color(1f, .45f, .75f), "Yellow" => new Color(1f, .93f, .35f),
        "Cyan" => new Color(.35f, .95f, 1f), _ => Color.white
    };

    internal static void Clear()
    {
        if (root != null) Object.Destroy(root);
        root = null;
        battle = null;
        labels.Clear();
    }
}
