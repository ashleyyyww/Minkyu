using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore.HostComponent;
using Il2CppAssets.Scripts.GameCore.Managers;
using Il2CppAssets.Scripts.GameCore.GameObjectLogics.GameObjectManager;
using Il2CppAssets.Scripts.PeroTools.Managers;
using Il2CppAssets.Scripts.UI.Panels;
using Il2CppGameLogic;
using Il2CppAssets.Scripts.UI.GameMain;
using UnityEngine;
using UnityEngine.UI;

namespace Minkyu.Mods;

internal static class SelectiveEffects
{
    internal static bool InBattle;
    private static readonly Dictionary<int, (Renderer Renderer, bool Enabled)> renderers = new();
    private static readonly Dictionary<int, (Graphic Graphic, bool Enabled)> graphics = new();
    private static readonly Dictionary<int, (Graphic Graphic, Color Color)> colors = new();
    private static readonly Dictionary<int, (Transform Transform, Vector3 Original, Vector3 Applied)> scales = new();
    private static readonly Dictionary<int, (GameObject Object, bool Active)> objects = new();
    private static readonly Dictionary<int, (Il2CppSpine.Unity.SkeletonAnimation Animation, float Alpha)> enemyAlpha = new();

    internal static void HideVisuals(GameObject root)
    {
        if (root == null) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderers.TryAdd(renderer.GetInstanceID(), (renderer, renderer.enabled));
            renderer.enabled = false;
        }
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            graphics.TryAdd(graphic.GetInstanceID(), (graphic, graphic.enabled));
            graphic.enabled = false;
        }
    }
    private static void HideUi(GameObject root)
    {
        if (root == null) return;
        objects.TryAdd(root.GetInstanceID(), (root, root.activeSelf));
        root.SetActive(false);
    }
    internal static void RestoreVisuals(GameObject root)
    {
        if (root == null || renderers.Count == 0 && graphics.Count == 0) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            if (renderers.Remove(renderer.GetInstanceID(), out var state)) renderer.enabled = state.Enabled;
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            if (graphics.Remove(graphic.GetInstanceID(), out var state)) graphic.enabled = state.Enabled;
    }
    internal static void RestoreEnemy(BaseEnemyObjectController enemy)
    {
        RestoreVisuals(enemy.gameObject);
        if (enemyAlpha.Remove(enemy.GetInstanceID(), out var state) && state.Animation != null && state.Animation.skeleton != null) state.Animation.skeleton.a = state.Alpha;
    }
    internal static void HideEnemy(BaseEnemyObjectController enemy)
    {
        var skeleton = enemy.m_SkeletonAnimation?.skeleton;
        if (skeleton == null) return;
        enemyAlpha.TryAdd(enemy.GetInstanceID(), (enemy.m_SkeletonAnimation, skeleton.a));
        skeleton.a = 0;
    }
    internal static void End()
    {
        InBattle = false;
        foreach (var state in renderers.Values) if (state.Renderer != null) state.Renderer.enabled = state.Enabled;
        foreach (var state in graphics.Values) if (state.Graphic != null) state.Graphic.enabled = state.Enabled;
        foreach (var state in colors.Values) if (state.Graphic != null) state.Graphic.color = state.Color;
        foreach (var state in objects.Values) if (state.Object != null) state.Object.SetActive(state.Active);
        foreach (var state in scales.Values)
            if (state.Transform != null && state.Transform.localScale == state.Applied) state.Transform.localScale = state.Original;
        foreach (var state in enemyAlpha.Values)
            if (state.Animation != null && state.Animation.skeleton != null) state.Animation.skeleton.a = state.Alpha;
        renderers.Clear();
        graphics.Clear();
        colors.Clear();
        objects.Clear();
        scales.Clear();
        enemyAlpha.Clear();
    }
    internal static bool Enabled => InBattle && Mod.On("Effects.Enabled");
    internal static bool Hide(string key) => Enabled && (Mod.On("Effects.All") || Mod.On("Effects." + key));
    internal static void EffectCreated(Effect effect, GameObject instance)
    {
        if (!Enabled || instance == null) return;
        var uid = effect?.uid ?? instance.name;
        var judgement = uid.Contains("Perfect", StringComparison.OrdinalIgnoreCase) || uid.Contains("Great", StringComparison.OrdinalIgnoreCase) || uid.Contains("Pass", StringComparison.OrdinalIgnoreCase);
        var hide = Mod.On("Effects.All")
            || judgement && Mod.On("Effects.Judgement")
            || Mod.On("Effects.Perfect") && uid.Contains("Perfect", StringComparison.OrdinalIgnoreCase)
            || Mod.On("Effects.Great") && uid.Contains("Great", StringComparison.OrdinalIgnoreCase)
            || Mod.On("Effects.Pass") && uid.Contains("Pass", StringComparison.OrdinalIgnoreCase)
            || Mod.On("Effects.GirlHit") && uid.Contains("girl_fx_atk", StringComparison.OrdinalIgnoreCase)
            || Mod.On("Effects.Hold") && uid.Contains("press_top_fx", StringComparison.OrdinalIgnoreCase)
            || Mod.On("Effects.Music") && (uid.Contains("fx_score", StringComparison.Ordinal) || uid.Contains("TxtScoreGet", StringComparison.Ordinal))
            || Mod.On("Effects.Heart") && (uid.Contains("fx_hp", StringComparison.Ordinal) || uid.Contains("TxtHpGet", StringComparison.Ordinal))
            || Mod.On("Effects.Boss") && uid.Contains("boss", StringComparison.OrdinalIgnoreCase)
            || Mod.On("Effects.Dust") && uid.Contains("dust_fx", StringComparison.OrdinalIgnoreCase)
            || Mod.On("Effects.Hurt") && uid.Contains("TxtHurt", StringComparison.OrdinalIgnoreCase)
            || Mod.On("Effects.Elfin") && uid.Contains("elfin", StringComparison.OrdinalIgnoreCase);
        if (hide) { HideVisuals(instance); return; }
        if (Mod.On("Effects.Stars") && uid.Contains("girl_fx_atk", StringComparison.OrdinalIgnoreCase))
        {
            var stars = instance.transform.Find("fx_star");
            if (stars != null) HideVisuals(stars.gameObject);
        }
        if (judgement)
        {
            var scale = Mod.Settings.Get<int>("Effects.JudgementSize") / 100f;
            var transform = instance.transform;
            var id = transform.GetInstanceID();
            var original = transform.localScale;
            // Pooled effects may still have our scale.
            if (scales.TryGetValue(id, out var old) && original == old.Applied) original = old.Original;
            var applied = original * scale;
            scales[id] = (transform, original, applied);
            transform.localScale = applied;
        }
    }
    internal static void BattleStart(PnlBattle panel)
    {
        End();
        InBattle = true;
        if (!Enabled) return;
        var comp = panel.CurrentBattleUIComp;
        if (comp != null)
        {
            if (Mod.On("Effects.Health") || Mod.On("Effects.Score"))
            {
                var before = panel.GetComponentsInChildren<RectTransform>(true)
                    .Select(t => (Object: t.gameObject, Active: t.gameObject.activeSelf)).ToArray();
                if (Mod.On("Effects.Health")) comp.HideHpAndFeverStateUI();
                if (Mod.On("Effects.Score")) comp.HideScoreValueUI();
                foreach (var state in before)
                    if (state.Object != null && state.Object.activeSelf != state.Active)
                        objects.TryAdd(state.Object.GetInstanceID(), state);
            }
            if (Mod.On("Effects.Progress") && comp.sldProgress != null) HideUi(comp.sldProgress.gameObject);
            if (Mod.On("Effects.Pause"))
            {
                // Keep the pause touch target; hide only its icon.
                var pause = panel.GetComponentsInChildren<Transform>(false)
                    .FirstOrDefault(t => t.name == "ImgPause" && t.parent?.name == "BtnPause");
                if (pause != null)
                    foreach (var graphic in pause.GetComponentsInChildren<Graphic>(true))
                    {
                        colors.TryAdd(graphic.GetInstanceID(), (graphic, graphic.color));
                        var color = graphic.color;
                        color.a = 0;
                        graphic.color = color;
                    }
            }
        }
        if (Mod.On("Effects.Combo"))
            foreach (var name in new[] { "PnlCommonUI", "PnlNewComboUI" })
                HideUi(GameObject.Find(name));
        if (Mod.On("Effects.Background") || Mod.On("Effects.FloorOnly"))
        {
            var scene = GameGlobal.gGameMusicScene;
            if (scene != null)
            {
                if (scene.scenes != null)
                    foreach (var root in scene.scenes)
                        if (root != null) HideStageChildren(root.transform, Mod.On("Effects.FloorOnly") && !Mod.On("Effects.Background"));
                else if (scene.scene != null) HideStageChildren(scene.scene.transform, Mod.On("Effects.FloorOnly") && !Mod.On("Effects.Background"));
            }
        }
        if (Mod.On("Effects.HitPoints"))
            foreach (var point in Resources.FindObjectsOfTypeAll<HitPointSubControl>())
                if (point.gameObject.activeInHierarchy) HideVisuals(point.gameObject);
        if (Mod.On("Effects.Girl"))
            foreach (var controller in UnityEngine.Resources.FindObjectsOfTypeAll<GirlActionController>())
                if (controller.gameObject.activeInHierarchy)
                {
                    HideVisuals(controller.go);
                    if (controller.ghostSk != null) HideVisuals(controller.ghostSk.gameObject);
                }
        if (Mod.On("Effects.HideElfin"))
            foreach (var elfin in UnityEngine.Resources.FindObjectsOfTypeAll<ElfinCreate>())
                if (elfin.gameObject.activeInHierarchy && elfin.m_SkeletonMecanim != null) HideVisuals(elfin.m_SkeletonMecanim.gameObject);
    }
    private static void HideStageChildren(Transform root, bool floorOnly)
    {
        // Disabling scene objects would also stop their gameplay scripts.
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var floor = false;
            for (var parent = renderer.transform; parent != null; parent = parent.parent)
            {
                if (parent.name.Contains("floor", StringComparison.OrdinalIgnoreCase) || parent.name.Contains("road", StringComparison.OrdinalIgnoreCase)) floor = true;
                if (parent == root) break;
            }
            if (floorOnly && floor) continue;
            renderers.TryAdd(renderer.GetInstanceID(), (renderer, renderer.enabled));
            renderer.enabled = false;
        }
    }
}

[HarmonyPatch(typeof(Effect), nameof(Effect.CreateInstance))]
internal static class EffectCreationPatch
{
    private static void Postfix(Effect __instance, GameObject __result) => Mod.Guard(() => SelectiveEffects.EffectCreated(__instance, __result));
}
[HarmonyPatch(typeof(PnlBattle), nameof(PnlBattle.GameStart))]
internal static class EffectBattleStart
{
    private static void Postfix(PnlBattle __instance) => Mod.Guard(() => SelectiveEffects.BattleStart(__instance));
}
[HarmonyPatch(typeof(PnlBattle), nameof(PnlBattle.OnDestroy))]
internal static class EffectBattleEnd
{
    private static void Prefix() => Mod.Guard(SelectiveEffects.End);
}
[HarmonyPatch(typeof(FeverEffectManager), nameof(FeverEffectManager.ActivateFever))]
internal static class FeverStartPatch
{
    private static void Postfix(FeverEffectManager __instance)
    {
        if (!SelectiveEffects.Enabled || GlobalDataBase.dbTouhou?.isBadApple == true) return;
        Mod.Guard(() =>
        {
            SelectiveEffects.RestoreVisuals(__instance.gameObject);
            if (Mod.On("Effects.Fever")) { SelectiveEffects.HideVisuals(__instance.gameObject); return; }
            if (Mod.On("Effects.FeverBackground")) SelectiveEffects.HideVisuals(__instance.m_Background);
            if (Mod.On("Effects.FeverStars") && __instance.m_Particles != null) foreach (var star in __instance.m_Particles) SelectiveEffects.HideVisuals(star);
        });
    }
}
[HarmonyPatch(typeof(FeverEffectManager), nameof(FeverEffectManager.CancelFeverEffect))]
internal static class FeverEndPatch
{
    private static void Postfix(FeverEffectManager __instance)
    {
        if (!SelectiveEffects.Enabled || !Mod.On("Effects.FeverTransition") || GlobalDataBase.dbTouhou?.isBadApple == true) return;
        Mod.Guard(() =>
        {
            if (__instance.m_EndParticles != null) foreach (var particle in __instance.m_EndParticles) SelectiveEffects.HideVisuals(particle);
            SelectiveEffects.HideVisuals(__instance.gameObject);
        });
    }
}
[HarmonyPatch(typeof(JudgeDisplay), nameof(JudgeDisplay.OnEnable))]
internal static class PerfectJudgementPatch
{
    private static void Prefix(JudgeDisplay __instance) => Mod.Guard(() => SelectiveEffects.RestoreVisuals(__instance.gameObject));
    private static void Postfix(JudgeDisplay __instance)
    {
        Mod.Guard(() =>
        {
            if (SelectiveEffects.Enabled && Mod.On("Effects.TruePerfect") && BattleRoleAttributeComponent.instance?.judgeState == 0)
                SelectiveEffects.HideVisuals(__instance.gameObject);
        });
    }
}
[HarmonyPatch(typeof(AttackEffectManager), nameof(AttackEffectManager.PlayAttackEffect))]
internal static class HitEffectPatch
{
    private static void Postfix(AttackEffectManager __instance)
    {
        Mod.Guard(() =>
        {
            if (SelectiveEffects.Hide("Hit")) SelectiveEffects.HideVisuals(__instance.m_PlayResult);
        });
    }
}
[HarmonyPatch(typeof(BaseEnemyObjectController), nameof(BaseEnemyObjectController.OnControllerAttacked))]
internal static class EnemyVisualPatch
{
    private static void Postfix(BaseEnemyObjectController __instance)
    {
        if (!SelectiveEffects.Enabled) return;
        Mod.Guard(() =>
        {
            if (Mod.On("Effects.All") || Mod.On("Effects.HitAnimation")) SelectiveEffects.HideEnemy(__instance);
            if (Mod.On("Effects.Hit")) SelectiveEffects.HideVisuals(__instance.transform.Find("out_fx")?.gameObject);
        });
    }
}
[HarmonyPatch(typeof(HitPointSubControl), nameof(HitPointSubControl.Awake))]
internal static class HitPointPatch
{
    private static void Postfix(HitPointSubControl __instance)
    {
        Mod.Guard(() =>
        {
            if (Mod.On("Effects.Enabled") && Mod.On("Effects.HitPoints")) SelectiveEffects.HideVisuals(__instance.gameObject);
        });
    }
}

[HarmonyPatch(typeof(BaseEnemyObjectController), nameof(BaseEnemyObjectController.Init))]
internal static class EnemyReusePatch
{
    private static void Prefix(BaseEnemyObjectController __instance) => Mod.Guard(() => SelectiveEffects.RestoreEnemy(__instance));
}

[HarmonyPatch(typeof(Effect), nameof(Effect.Reset))]
internal static class EffectReusePatch
{
    private static void Prefix(GameObject go) => Mod.Guard(() => SelectiveEffects.RestoreVisuals(go));
}

[HarmonyPatch(typeof(StatisticsManager), nameof(StatisticsManager.OnBattleEnd))]
internal static class EffectResultsCleanup
{
    private static void Prefix() => Mod.Guard(SelectiveEffects.End);
}
