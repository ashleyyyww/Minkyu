using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore.Managers;
using Il2CppAssets.Scripts.Structs;
using Il2CppInterop.Common;

namespace CustomAlbums.Android;

internal static class AchievementIsolationHook
{
    // Harmony misboxes this ref struct on ARM64; use the native ABI.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RecordDelegate(IntPtr self, IntPtr args, byte isSuccess, IntPtr method);
    private static readonly RecordDelegate Detour = Record;
    private static RecordDelegate Original;
    private static bool Installed;
    [DllImport("libdobby.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern int DobbyHook(IntPtr address, IntPtr replacement, out IntPtr trampoline);
    public static unsafe void Attach()
    {
        if (Installed)
            return;
        var method = AccessTools.Method(typeof(AchievementManager), nameof(AchievementManager.RecordBattleArgs), new[] { typeof(GlobalAchievementArgs).MakeByRefType(), typeof(bool) });
        if (method == null || method.ReturnType != typeof(void))
            throw new MissingMethodException("Expected void RecordBattleArgs(ref GlobalAchievementArgs,bool)");
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
        if (field == null)
            throw new MissingFieldException("AchievementManager.RecordBattleArgs generated MethodInfo field");
        var info = (IntPtr)field.GetValue(null);
        if (info == IntPtr.Zero)
            throw new InvalidOperationException("Null RecordBattleArgs MethodInfo");
        var target = *(IntPtr*)info;
        if (target == IntPtr.Zero)
            throw new InvalidOperationException("Null RecordBattleArgs native target");
        var status = DobbyHook(target, Marshal.GetFunctionPointerForDelegate(Detour), out var trampoline);
        if (status != 0 || trampoline == IntPtr.Zero || trampoline == target)
            throw new InvalidOperationException($"RecordBattleArgs DobbyHook failed status={status} trampoline=0x{trampoline:x}");
        Original = Marshal.GetDelegateForFunctionPointer<RecordDelegate>(trampoline);
        Installed = true;
        Mod.Log?.Msg("CA_ACHIEVEMENT_HOOK_ATTACHED");
    }

    private static void Record(IntPtr self, IntPtr args, byte isSuccess, IntPtr method)
    {
        var skip = false;
        try
        {
            var uid = Mod.Ready ? GlobalDataBase.dbBattleStage?.musicUid : null;
            skip = Mod.Ready && uid != null && AlbumCatalog.Albums.Any(album => album.Uid == uid);
        }
        catch (Exception ex)
        {
            Mod.Log?.Error("CA_ACHIEVEMENT_ISOLATION_CHECK_FAILED " + ex);
            skip = false;
        }

        if (!skip)
            Original(self, args, isSuccess, method);
    }
}
