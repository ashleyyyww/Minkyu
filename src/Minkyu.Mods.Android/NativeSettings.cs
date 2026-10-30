using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.UI.PnlOptions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Minkyu.Mods;

internal static class NativeSettings
{
    private static OptionSelect options;
    private static GameObject window, inactiveTemplates, content;
    private static CanvasGroup optionsInput;
    private static bool optionsInteractable, optionsBlocksRaycasts;
    private static float nextUpdateNoticeCheck;
    private static Transform toggleTemplate, buttonTemplate, sliderTemplate;
    private static Text titleTemplate;
    private static readonly List<GameObject> generated = new();
    private static readonly List<GameObject> sectionButtons = new();
    private static string selectedSection = "Info+";
    private static int page;
    private static int infoField;
    private static int sectionPage;
    private static Font nativeFont;
    private static Sprite menuIcon;
    private static bool showing, renderPending;
    private static Graphic welcomeInput;
    private static Transform welcomePanel;
    private static float nextWelcomeCheck;
    private static bool welcomeRaycastOriginal;
    private static CanvasGroup welcomeAccountStrip;
    private static float welcomeStripAlpha;
    private static bool welcomeStripInteractable, welcomeStripBlocksRaycasts;
    private static TouchScreenKeyboard keyboard;
    private static Setting editing;
    private const int RowsPerPage = 6;
    private static readonly string[] Sections = { "Info+", "Info+ display", "Scoreboard Characters", "Rank Preview", "SelectiveEffects", "Fever", "Judgements", "Hit effects", "Other effects", "Stage", "Characters", "Interface", "Leaderboards", "Account", "Custom songs" };

    internal static void Attach(OptionSelect instance)
    {
        options = instance;
        nativeFont = instance.transform.Find("Toggles/BtnPlaySetting")?.GetComponentInChildren<Text>(true)?.font;
        if (instance.transform.Find("Toggles/Minkyu.Settings") != null) return;
        var source = instance.transform.Find("Toggles/BtnPlaySetting");
        if (source == null) throw new InvalidOperationException("Native play-settings button unavailable");
        var entry = Object.Instantiate(source.gameObject, source.parent);
        entry.name = "Minkyu.Settings";
        Strip(entry);
        var rect = entry.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(787.5f, -112.5f);
        foreach (var text in entry.GetComponentsInChildren<Text>(true)) text.text = "MOD SETTINGS";
        var icon = entry.transform.Find("Image")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.sprite = LoadIcon();
            icon.preserveAspect = true;
            icon.color = Color.white;
            var aspect = icon.GetComponent<AspectRatioFitter>();
            if (aspect != null) aspect.aspectRatio = 1;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, .5f);
            icon.rectTransform.anchoredPosition = new Vector2(0, 25);
            icon.rectTransform.sizeDelta = new Vector2(150, 150);
        }
        var button = entry.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener((UnityAction)(Action)(() => Mod.Guard(Open)));
        entry.SetActive(true);
        Mod.Log.Msg("Mod Settings added to game options");
    }

    private static Sprite LoadIcon()
    {
        if (menuIcon != null) return menuIcon;
        using var stream = typeof(NativeSettings).Assembly.GetManifestResourceStream("Minkyu.ModIcon.png")
            ?? throw new FileNotFoundException("Mod Settings icon missing");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes.ToArray()))
        {
            Object.Destroy(texture);
            throw new InvalidDataException("Mod Settings icon could not be loaded");
        }
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        menuIcon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        menuIcon.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return menuIcon;
    }

    private static void Open()
    {
        if (window == null) BuildWindow();
        optionsInput = options.GetComponent<CanvasGroup>() ?? options.gameObject.AddComponent<CanvasGroup>();
        optionsInteractable = optionsInput.interactable;
        optionsBlocksRaycasts = optionsInput.blocksRaycasts;
        optionsInput.interactable = optionsInput.blocksRaycasts = false;
        showing = true;
        window.SetActive(true);
        window.transform.SetAsLastSibling();
        Render();
    }
    private static void BuildWindow()
    {
        var native = Resources.FindObjectsOfTypeAll<PnlPlaySetting>().FirstOrDefault();
        if (native == null) throw new InvalidOperationException("Native play-settings panel unavailable");
        var wasActive = native.gameObject.activeSelf;
        if (wasActive) native.gameObject.SetActive(false);
        window = Object.Instantiate(native.gameObject, native.transform.parent);
        window.name = "Minkyu.SettingsPanel";
        if (wasActive) native.gameObject.SetActive(true);
        Strip(window);
        inactiveTemplates = new GameObject("Minkyu.Templates");
        inactiveTemplates.transform.SetParent(window.transform, false);
        inactiveTemplates.SetActive(false);
        toggleTemplate = CopyTemplate(window.transform.Find("AreaAdvancedSetting/SettingElements/TglAdvancedJudge"));
        sliderTemplate = CopyTemplate(window.transform.Find("AreaNormalSetting/SettingElements/HitSfxSetting"));
        buttonTemplate = CopyTemplate(window.transform.Find("AreaNormalSetting/SettingElements/AreaGameSceneSetting/BtnFeverBgSetting")
            ?? window.transform.Find("AreaNormalSetting/SettingElements/BtnFeverBgSetting")
            ?? native.m_BtnFeverBgSetting.transform);
        titleTemplate = window.transform.Find("ImgTitle").GetComponentInChildren<Text>(true);
        titleTemplate.text = "MOD SETTINGS";
        foreach (var name in new[] { "AreaAdvancedSetting", "AreaNormalSetting", "ChildrenSettingPanel" })
        {
            var child = window.transform.Find(name);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }
        var back = window.transform.Find("BtnBack").GetComponent<Button>();
        back.onClick = new Button.ButtonClickedEvent();
        back.onClick.AddListener((UnityAction)(Action)Close);
        content = new GameObject("Minkyu.Content", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        content.transform.SetParent(window.transform, false);
        var root = content.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.sizeDelta = new Vector2(2300, 800);
    }
    private static Transform CopyTemplate(Transform source)
    {
        if (source == null) throw new InvalidOperationException("Native settings control unavailable");
        var clone = Object.Instantiate(source.gameObject, inactiveTemplates.transform);
        Strip(clone);
        return clone.transform;
    }
    private static void Strip(GameObject root)
    {
        // Keep Unity controls and pointer animations; remove the original callbacks.
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;
            var name = behaviour.GetIl2CppType().FullName;
            if (name.StartsWith("UnityEngine.UI.", StringComparison.Ordinal) || name is "ButtonPointerEnter" or "SelectToChangeState" or "ToggleAnimation") continue;
            Object.DestroyImmediate(behaviour);
        }
        foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
        {
            selectable.interactable = true;
            if (selectable.targetGraphic != null) selectable.targetGraphic.raycastTarget = true;
        }
        foreach (var button in root.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
        foreach (var toggle in root.GetComponentsInChildren<Toggle>(true)) toggle.onValueChanged = new Toggle.ToggleEvent();
        foreach (var slider in root.GetComponentsInChildren<Slider>(true)) slider.onValueChanged = new Slider.SliderEvent();
        foreach (var group in root.GetComponentsInChildren<CanvasGroup>(true))
        {
            group.alpha = 1;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }
    private static void Close()
    {
        showing = false;
        window?.SetActive(false);
        if (optionsInput != null)
        {
            optionsInput.interactable = optionsInteractable;
            optionsInput.blocksRaycasts = optionsBlocksRaycasts;
        }
    }
    private static void HideUpdateNotice()
    {
        if (Time.unscaledTime < nextUpdateNoticeCheck) return;
        nextUpdateNoticeCheck = Time.unscaledTime + .25f;
        // The Android prefab can lack PnlNewVersionTip; find it by its object name.
        var notice = options != null ? options.transform.Find("PrefNewVersionTip(Clone)")?.gameObject : null;
        if (notice == null) notice = GameObject.Find("PrefNewVersionTip(Clone)");
        if (notice != null && notice.activeSelf) notice.SetActive(false);
    }
    internal static void Tick()
    {
        FixWelcomeInput();
        HideUpdateNotice();
        if (showing && (options == null || !options.gameObject.activeInHierarchy)) Close();
        if (showing && Input.GetKeyDown(KeyCode.Escape)) Close();
        if (showing && renderPending)
        {
            renderPending = false;
            Render();
        }
        if (keyboard == null) return;
        if (keyboard.status == TouchScreenKeyboard.Status.Done)
        {
            Mod.Guard(() => Mod.Settings.Set(editing.Key, keyboard.text));
            keyboard = null;
            Render();
        }
        else if (keyboard.status is TouchScreenKeyboard.Status.Canceled or TouchScreenKeyboard.Status.LostFocus) keyboard = null;
    }
    private static void FixWelcomeInput()
    {
        if (Time.unscaledTime < nextWelcomeCheck) return;
        nextWelcomeCheck = Time.unscaledTime + .25f;
        if (options == null && GameObject.Find("UI/Standerd/PnlNavigation") == null) return;
        if (welcomeInput == null)
        {
            var account = GameObject.Find("MDAccountCanvas");
            welcomeInput = account?.transform.Find("PnlWelcomeAccount/BtnLogin")?.GetComponent<Graphic>();
            if (welcomeInput == null) return;
            welcomeRaycastOriginal = welcomeInput.raycastTarget;
        }
        welcomePanel = GameObject.Find("UI/UI/PnlWelcome")?.transform;
        var atTitle = welcomePanel != null && welcomePanel.gameObject.activeInHierarchy;
        // Canceled sign-in can leave an invisible login button blocking the whole screen.
        welcomeInput.raycastTarget = welcomeRaycastOriginal && atTitle && !showing;
        if (welcomeAccountStrip == null)
        {
            var strip = welcomeInput.transform.parent.Find("BtnChangeAccount");
            if (strip != null)
            {
                welcomeAccountStrip = strip.GetComponent<CanvasGroup>() ?? strip.gameObject.AddComponent<CanvasGroup>();
                welcomeStripAlpha = welcomeAccountStrip.alpha;
                welcomeStripInteractable = welcomeAccountStrip.interactable;
                welcomeStripBlocksRaycasts = welcomeAccountStrip.blocksRaycasts;
            }
        }
        if (welcomeAccountStrip != null)
        {
            welcomeAccountStrip.alpha = atTitle ? welcomeStripAlpha : 0;
            welcomeAccountStrip.interactable = atTitle && welcomeStripInteractable;
            welcomeAccountStrip.blocksRaycasts = atTitle && welcomeStripBlocksRaycasts;
        }
    }

    private static void Render()
    {
        foreach (var go in generated)
            if (go != null)
            {
                go.SetActive(false);
                Object.Destroy(go);
            }
        generated.Clear();
        for (var i = 0; i < Sections.Length; i++)
        {
            var section = Sections[i];
            GameObject button;
            if (i < sectionButtons.Count && sectionButtons[i] != null) button = sectionButtons[i];
            else
            {
                button = AddButton("Minkyu.Section." + i, section, new Vector2(-920, 285 - i % 8 * 90), new Vector2(395, 78), () =>
                {
                    selectedSection = section;
                    page = 0;
                    renderPending = true;
                });
                generated.Remove(button);
                if (i < sectionButtons.Count) sectionButtons[i] = button;
                else sectionButtons.Add(button);
            }
            button.SetActive(i / 8 == sectionPage);
            button.GetComponent<Image>().color = section == selectedSection ? new Color(1f, .06f, .52f) : new Color(.20f, .015f, .25f);
            var text = button.GetComponentInChildren<Text>(true);
            text.font = nativeFont;
            text.raycastTarget = false;
            text.color = Color.white;
            text.fontSize = 32;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 28;
            text.resizeTextMaxSize = 32;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(10, 2);
            text.rectTransform.offsetMax = new Vector2(-10, -2);
        }
        AddButton("Minkyu.CategoryPage", $"{sectionPage + 1} / 2  >", new Vector2(-920, -445), new Vector2(395, 65), () =>
        {
            sectionPage = 1 - sectionPage;
            renderPending = true;
        });
        if (selectedSection == "Account")
        {
            var signedIn = PeroAccount.IsLoggedIn;
            AddButton(signedIn ? "Minkyu.ChangeName" : "Minkyu.PeroLogin", signedIn ? "Change name" : "Sign in with Pero", new Vector2(220, 180), new Vector2(1600, 82), () =>
            {
                Close();
                if (signedIn) PeroAccount.ChangeName();
                else PeroAccount.Open();
            });
            return;
        }
        var separateFields = selectedSection == "Info+ display" && Mod.Settings.Get<string>("Info.Layout") == "Info+";
        var definitions = ModSettings.Definitions.Where(s => s.Section == selectedSection &&
            (selectedSection != "Info+" || Mod.Settings.Get<string>("Info.Layout") == "Compact" || s.Key is "Info.Enabled" or "Info.Layout" or "Info.EarlyLate" or "Info.AccuracyMode" or "Info.BestCriteria" or "Info.ReplaceResultMisses") &&
            (separateFields ? s.Key.StartsWith("Info.Field." + InfoFields.All[infoField].Id + ".", StringComparison.Ordinal)
                : !s.Key.StartsWith("Info.Field.", StringComparison.Ordinal))).ToArray();
        var rowsPerPage = separateFields ? 5 : RowsPerPage;
        if (separateFields)
            AddButton("Minkyu.InfoArea", "Area: " + InfoFields.All[infoField].Name + "  >", new Vector2(220, 180), new Vector2(1600, 78), () =>
            {
                infoField = (infoField + 1) % InfoFields.All.Length;
                page = 0;
                renderPending = true;
            });
        var pages = Math.Max(1, (definitions.Length + rowsPerPage - 1) / rowsPerPage);
        page = Math.Clamp(page, 0, pages - 1);
        var heading = UiText.Create("Minkyu.SectionTitle", content.transform, titleTemplate);
        generated.Add(heading.gameObject);
        heading.text = selectedSection;
        heading.color = titleTemplate.color;
        heading.fontSize = 38;
        heading.alignment = TextAnchor.MiddleLeft;
        Position(heading.rectTransform, new Vector2(220, 285), new Vector2(1600, 64));
        for (var row = 0; row < rowsPerPage && page * rowsPerPage + row < definitions.Length; row++)
        {
            var setting = definitions[page * rowsPerPage + row];
            var position = new Vector2(220, 180 - (row + (separateFields ? 1 : 0)) * 92);
            if (setting.IsToggle) AddToggle(setting, position);
            else if (setting.Default is int) AddSlider(setting, position);
            else
            {
                var current = Mod.Settings.Get<string>(setting.Key);
                GameObject option = null;
                option = AddButton("Minkyu.Option." + setting.Key, setting.Label + ": " + (current.Length == 0 ? "Default" : current), position, new Vector2(1600, 78), () =>
                {
                    if (setting.Choices != null)
                    {
                        var index = Array.IndexOf(setting.Choices, Mod.Settings.Get<string>(setting.Key));
                        var next = setting.Choices[(index + 1) % setting.Choices.Length];
                        Mod.Settings.Set(setting.Key, next);
                        if (setting.Key == "Info.Layout") renderPending = true;
                        option.GetComponentInChildren<Text>(true).text = setting.Label + ": " + next;
                    }
                    else
                    {
                        editing = setting;
                        keyboard = TouchScreenKeyboard.Open(current, TouchScreenKeyboardType.Default, false, true, false, false, "{acc}  {stats}  {song}", 512);
                    }
                });
            }
        }
        if (pages > 1)
        {
            AddButton("Minkyu.PreviousPage", "<", new Vector2(550, -390), new Vector2(120, 56), () => { page = (page + pages - 1) % pages; renderPending = true; });
            AddButton("Minkyu.NextPage", $"{page + 1} / {pages}  >", new Vector2(850, -390), new Vector2(260, 56), () => { page = (page + 1) % pages; renderPending = true; });
        }
    }
    private static GameObject AddButton(string name, string label, Vector2 position, Vector2 size, Action click)
    {
        var go = Object.Instantiate(buttonTemplate.gameObject, content.transform);
        go.name = name;
        generated.Add(go);
        Position(go.GetComponent<RectTransform>(), position, size);
        foreach (var text in go.GetComponentsInChildren<Text>(true))
        {
            text.text = label;
            text.font = nativeFont;
            text.raycastTarget = false;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 26;
            text.resizeTextMaxSize = 32;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Position(text.rectTransform, Vector2.zero, new Vector2(size.x - 40, size.y - 8));
        }
        var button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener((UnityAction)(Action)(() => Mod.Guard(click)));
        go.SetActive(true);
        return go;
    }
    private static void AddToggle(Setting setting, Vector2 position)
    {
        var go = Object.Instantiate(toggleTemplate.gameObject, content.transform);
        go.name = "Minkyu.Option." + setting.Key;
        generated.Add(go);
        Position(go.GetComponent<RectTransform>(), position, new Vector2(1600, 82));
        var toggles = go.GetComponentsInChildren<Toggle>(true).ToArray();
        var value = Mod.On(setting.Key);
        foreach (var text in go.GetComponentsInChildren<Text>(true))
        {
            text.font = nativeFont;
            text.raycastTarget = false;
            text.color = Color.white;
            if (text.transform.parent.GetComponent<Toggle>() != null)
            {
                text.raycastTarget = true;
                text.text = text.transform.parent.name.Contains("On", StringComparison.Ordinal) ? "ON" : "OFF";
                text.fontSize = 32;
                continue;
            }
            text.text = setting.Label;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 26;
            text.resizeTextMaxSize = 32;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Position(text.rectTransform, new Vector2(-270, 0), new Vector2(980, 75));
            text.alignment = TextAnchor.MiddleLeft;
        }
        foreach (var toggle in toggles)
        {
            var isOn = toggle.name.Contains("On", StringComparison.Ordinal);
            toggle.group = null;
            toggle.SetIsOnWithoutNotify(isOn ? value : !value);
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.onValueChanged.AddListener((UnityAction<bool>)(Action<bool>)(checkedNow =>
            {
                if (!checkedNow) { toggle.SetIsOnWithoutNotify(true); return; }
                Mod.Guard(() =>
                {
                    Mod.Settings.Set(setting.Key, isOn);
                    foreach (var other in toggles) other.SetIsOnWithoutNotify(other == toggle);
                });
            }));
        }
        go.SetActive(true);
    }
    private static void AddSlider(Setting setting, Vector2 position)
    {
        var go = Object.Instantiate(sliderTemplate.gameObject, content.transform);
        go.name = "Minkyu.Option." + setting.Key;
        generated.Add(go);
        Position(go.GetComponent<RectTransform>(), position, new Vector2(1600, 82));
        var value = Mod.Settings.Get<int>(setting.Key);
        var text = go.GetComponentsInChildren<Text>(true).FirstOrDefault();
        if (text != null)
        {
            text.transform.SetParent(go.transform, false);
            text.text = setting.Label + ": " + value;
            text.font = nativeFont;
            text.raycastTarget = false;
            text.fontSize = 32;
            text.color = Color.white;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 26;
            text.resizeTextMaxSize = 32;
            text.alignment = TextAnchor.MiddleLeft;
            Position(text.rectTransform, new Vector2(-475, 0), new Vector2(570, 75));
        }
        foreach (var other in go.GetComponentsInChildren<Text>(true))
            if (other != text) other.gameObject.SetActive(false);
        var sliderArea = go.transform.Find("SldHitSfxVolume")?.GetComponent<RectTransform>();
        if (sliderArea != null)
        {
            Position(sliderArea, new Vector2(300, 0), new Vector2(994, 75));
            sliderArea.localScale = new Vector3(.85f, .85f, 1);
        }
        var slider = go.GetComponentInChildren<Slider>(true);
        slider.minValue = setting.Minimum;
        slider.maxValue = setting.Maximum;
        slider.wholeNumbers = true;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged = new Slider.SliderEvent();
        slider.onValueChanged.AddListener((UnityAction<float>)(Action<float>)(next => Mod.Guard(() =>
        {
            Mod.Settings.Set(setting.Key, (int)next);
            if (text != null) text.text = setting.Label + ": " + (int)next;
        })));
        foreach (var button in go.GetComponentsInChildren<Button>(true))
        {
            var delta = button.name.Contains("Next", StringComparison.Ordinal) ? 1 : -1;
            button.onClick = new Button.ButtonClickedEvent();
            if (button.name.Contains("Tip", StringComparison.Ordinal)) button.gameObject.SetActive(false);
            else button.onClick.AddListener((UnityAction)(Action)(() => slider.value = Math.Clamp(slider.value + delta, setting.Minimum, setting.Maximum)));
        }
        go.SetActive(true);
    }
    private static void Position(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
}

[HarmonyPatch(typeof(OptionSelect), nameof(OptionSelect.OnAwake))]
internal static class SettingsEntryPatch
{
    private static void Postfix(OptionSelect __instance) => Mod.Guard(() => NativeSettings.Attach(__instance));
}
