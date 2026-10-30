using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.UI.Panels;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Minkyu.Mods;

internal static class ScoreboardLoadout
{
    private sealed record Pose(RectTransform Rect, Vector2 Min, Vector2 Max, Vector2 Pivot, Vector2 Size, Vector3 Position, Vector3 Scale)
    {
        internal void Restore()
        {
            if (Rect == null) return;
            Rect.anchorMin = Min;
            Rect.anchorMax = Max;
            Rect.pivot = Pivot;
            Rect.sizeDelta = Size;
            Rect.localPosition = Position;
            Rect.localScale = Scale;
        }
    }

    private static Transform group, artist;
    private static GameObject portraits;
    private static bool artistActive;
    private static bool rootActive;
    private static Button selectButton;
    private static Button.ButtonClickedEvent nativeSelect;
    private static Button.ButtonClickedEvent directSelect;
    private static readonly Dictionary<int, Pose> original = new();

    internal static void Refresh(PnlRank panel)
    {
        if (CustomLeaderboards.MenuClosing || panel == null || !panel.isActiveAndEnabled) return;
        var preparation = panel.GetComponentInParent<PnlPreparation>();
        var top = preparation?.transform.Find("RightRoot/Top");
        if (top == null) return;
        if (group != top)
        {
            Restore();
            original.Clear();
            group = top;
            artist = group.Find("RootLevelConfigShow/ImgArtistMask");
            artistActive = artist != null && artist.gameObject.activeSelf;
            rootActive = top.Find("RootLevelConfigShow")?.gameObject.activeSelf == true;
            portraits = null;
            selectButton = FindSelectButton(top);
            nativeSelect = selectButton?.onClick;
            directSelect = null;
        }
        if (!Mod.On("Characters.Enabled"))
        {
            Restore();
            return;
        }
        var header = panel.transform.Find("Mask/ImgBaseDarkP/ImgTittleBaseP");
        var refresh = header?.Find("BtnRefresh");
        var root = group.Find("RootLevelConfigShow");
        if (header == null || refresh == null || root == null) return;
        root.gameObject.SetActive(true);

        Remember(group);
        group.localPosition = group.parent.InverseTransformPoint(header.TransformPoint(new Vector3(refresh.localPosition.x - 220, refresh.localPosition.y, 0)));
        SetRect(root, new Vector2(-40, 0), new Vector2(180, 40));
        SetRect(root.Find("ImgLevelLockBg"), new Vector2(-65, 0), new Vector2(32, 32));
        SetRect(root.Find("BtnLockLevelConfig"), new Vector2(-65, 0), new Vector2(44, 48));
        SetRect(root.Find("BtnOpenPnllevelConfig"), new Vector2(20, 0), new Vector2(125, 48));
        if (selectButton != null)
        {
            if (directSelect == null)
            {
                directSelect = new Button.ButtonClickedEvent();
                directSelect.AddListener((UnityAction)(Action)(() => Mod.Guard(OpenSelection)));
            }
            selectButton.onClick = directSelect;
        }
        var random = group.Find("ImgRandomBg");
        SetRect(random, new Vector2(95, 0), new Vector2(55, 40));
        SetRect(random?.Find("ImgRandomFlagBg"), Vector2.zero, new Vector2(35, 35));
        SetRect(random?.Find("BtnRandomReset"), Vector2.zero, new Vector2(60, 48));
        if (artist != null) artist.gameObject.SetActive(false);

        if (portraits == null)
        {
            portraits = new GameObject("Minkyu.Loadout", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
            portraits.transform.SetParent(root, false);
            AddImage("Character");
            AddImage("Elfin");
        }
        SetRect(portraits.transform, new Vector2(20, 0), new Vector2(80, 40));
        portraits.SetActive(true);
        var character = portraits.transform.Find("Character").GetComponent<Image>();
        var elfin = portraits.transform.Find("Elfin").GetComponent<Image>();
        character.sprite = ScoreboardCharacters.Portrait(true, DataHelper.selectedRoleIndex);
        elfin.sprite = ScoreboardCharacters.Portrait(false, DataHelper.selectedElfinIndex);
        character.enabled = character.sprite != null;
        elfin.enabled = elfin.sprite != null;
    }

    private static Button FindSelectButton(Transform top) => top.Find("RootLevelConfigShow/BtnOpenPnllevelConfig")?.GetComponent<Button>();

    internal static void EnsureVisible(PnlRank panel)
    {
        if (!Mod.On("Characters.Enabled") || group == null) return;
        var root = group.Find("RootLevelConfigShow");
        if (root != null && !root.gameObject.activeSelf) Refresh(panel);
    }

    private static void OpenSelection()
    {
        // The native preset popup can open empty on Android; use its Custom action.
        PnlLevelConfig.ShowLevelConfigSelect(new LevelConfigSelectionData
        {
            selectionType = LevelConfigSelectionData.SelectionType.StageSelect,
            isSetSelectInfo = false,
            selectedCharacterIndex = DataHelper.selectedRoleIndex,
            selectedElfinIndex = DataHelper.selectedElfinIndex
        });
    }

    private static void AddImage(string name)
    {
        var imageObject = new GameObject(name, Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(), Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(), Il2CppInterop.Runtime.Il2CppType.Of<Image>());
        imageObject.transform.SetParent(portraits.transform, false);
        var image = imageObject.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        SetRect(image.transform, new Vector2(name == "Character" ? -20 : 20, 0), new Vector2(40, 40));
    }

    private static void Remember(Transform transform)
    {
        var rect = transform?.TryCast<RectTransform>();
        if (rect != null)
            original.TryAdd(rect.GetInstanceID(), new(rect, rect.anchorMin, rect.anchorMax, rect.pivot, rect.sizeDelta, rect.localPosition, rect.localScale));
    }

    private static void SetRect(Transform transform, Vector2 position, Vector2 size)
    {
        var rect = transform?.TryCast<RectTransform>();
        if (rect == null) return;
        Remember(transform);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Restore()
    {
        foreach (var pose in original.Values) pose.Restore();
        if (portraits != null) portraits.SetActive(false);
        if (artist != null) artist.gameObject.SetActive(artistActive);
        if (group != null) group.Find("RootLevelConfigShow")?.gameObject.SetActive(rootActive);
        if (selectButton != null && nativeSelect != null) selectButton.onClick = nativeSelect;
    }
}
