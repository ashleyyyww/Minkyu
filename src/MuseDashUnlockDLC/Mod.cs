using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.Helpers;
using Il2CppAssets.Scripts.Utils;
using Il2CppAccount;
using Il2Cpp;

[assembly: MelonInfo(typeof(MuseDashUnlockDLC.Mod), "MuseDashUnlockDLC", "0.1.0", "Minkyu contributors")]
namespace MuseDashUnlockDLC;

public sealed class Mod : MelonMod
{
    internal static bool Enabled;
    public override void OnInitializeMelon()
    {
        // Keep the existing preference key so updates preserve the saved setting.
        var category = MelonPreferences.CreateCategory("MinkyuLocalUnlock");
        Enabled = category.CreateEntry("Enabled", true, "Enable MuseDashUnlockDLC").Value;
        LoggerInstance.Msg($"MuseDashUnlockDLC initialized enabled={Enabled} purchaseDataWrites=false");
    }

    internal static IEnumerable<MethodBase> Find(Type type, params string[] names)
    {
        var wanted = new HashSet<string>(names, StringComparer.Ordinal);
        return AccessTools.GetDeclaredMethods(type).Where(m => wanted.Contains(m.Name) && m.ReturnType == typeof(bool) && m.GetParameters().All(p => !p.ParameterType.IsByRef));
    }
}

[HarmonyPatch]
internal static class LocalAvailability
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var method in Mod.Find(typeof(IAPHelper), "FastCheckUnlock", "HasBuyMusePlus", "HasBuyJustAsPlanned", "get_hasBuyJustAsPlanned", "get_isAllUnlock", "CheckRoleIsUnlocked", "IsItemUnlockByVip", "SafeFastCheck", "FastCheckUnlockCurrentVip", "FastCheckUnlockVip"))
            yield return method;
        foreach (var method in Mod.Find(typeof(DataHelper), "get_isSelectedMusicUnlock", "get_isSelectedUnlockMaster", "get_isUnlockAllMaster", "get_isUnlockAllBaseAlbum"))
            yield return method;
        foreach (var method in Mod.Find(typeof(MusicTagHelper), "CheckAlbumBuyAndUnlock", "CheckMusicBuyAndUnlock", "CheckMusicUnlockMaster", "CheckSkinIsBuy"))
            yield return method;
        foreach (var method in Mod.Find(typeof(ItemUtils), "IsUnlock"))
            yield return method;
        foreach (var method in Mod.Find(typeof(SavedRoleElfinConfigModel), "CheckRoleIsUnlocked", "CheckElfinIsUnlocked"))
            yield return method;
    }

    private static bool Prefix(ref bool __result)
    {
        if (!Mod.Enabled)
            return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(GameAccountSystem), nameof(GameAccountSystem.SendToUrl))]
internal static class LocalScoreUploadGuard
{
    private static bool Prefix(string url, string method)
    {
        if (!Mod.Enabled || string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            return true;
        return !(url?.Contains("leaderboard", StringComparison.OrdinalIgnoreCase) == true || url?.Contains("high-score", StringComparison.OrdinalIgnoreCase) == true || url?.Contains("statistics", StringComparison.OrdinalIgnoreCase) == true);
    }
}
