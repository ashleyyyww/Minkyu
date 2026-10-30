using HarmonyLib;
using Il2CppAssets.Scripts.Helpers;

namespace Minkyu.Mods;

[HarmonyPatch(typeof(VersionHelper))]
internal static class VersionCheck
{
    [HarmonyPrefix, HarmonyPatch(nameof(VersionHelper.CheckVersion))]
    private static bool Check(Il2CppSystem.Action<bool> callback)
    {
        // Match the game's offline result and complete the waiting hotfix phase.
        callback?.Invoke(false);
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(nameof(VersionHelper.CheckNeedUpdate))]
    private static bool NeedsUpdate(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch]
internal static class VersionPrompt
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (var name in new[] {
            nameof(VersionHelper.StartReqVersionTimer),
            nameof(VersionHelper.CheckUpdateStateAndShowConfirm),
            nameof(VersionHelper.ShowUpdateConfirmPopup),
            nameof(VersionHelper.ShowUpdateTip)
        })
            yield return AccessTools.Method(typeof(VersionHelper), name)
                ?? throw new MissingMethodException(typeof(VersionHelper).FullName, name);
    }

    private static bool Prefix() => false;
}
