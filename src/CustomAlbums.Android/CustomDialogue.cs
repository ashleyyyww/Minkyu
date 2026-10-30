using CustomAlbums.Core;
using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore;
using Il2CppAssets.Scripts.Structs;
using Newtonsoft.Json;

namespace CustomAlbums.Android;

internal static class CustomDialogue
{
    private static bool version2;
    private static int index;
    private static Il2CppSystem.Collections.Generic.List<GameDialogArgs> events;

    internal static void Load(AlbumRecord album, int difficulty, StageInfo stage)
    {
        version2 = false;
        events = null;
        index = 0;
        if (!Mod.DialogueEnabled) return;
        try
        {
            Newtonsoft.Json.Linq.JObject dialogue;
            lock (album.Archive)
            {
                album.CheckUnchanged();
                dialogue = ChartMedia.ReadDialogue(album.Archive, difficulty);
            }
            if (dialogue == null) return;
            version2 = dialogue.Value<int?>("version") == 2;
            dialogue.Remove("version");
            stage.dialogEvents = GameDialogArgs.Load(dialogue.ToString(Formatting.None));
            var language = DataHelper.userLanguage;
            if (stage.dialogEvents.ContainsKey(language)) events = stage.dialogEvents[language];
            else if (stage.dialogEvents.ContainsKey("English"))
            {
                events = stage.dialogEvents["English"];
                stage.dialogEvents[language] = events;
            }
            Mod.Log.Msg("CA_DIALOGUE_LOADED events=" + (events?.Count ?? 0));
        }
        catch (Exception ex)
        {
            version2 = false;
            events = null;
            stage.dialogEvents = null;
            Mod.Log.Warning("Custom dialogue skipped: " + ex.Message);
        }
    }

    internal static void Show(DialogSubControl control)
    {
        if (!Mod.DialogueEnabled || !CustomDataIsolation.InCustomBattle || !version2 || events == null || index >= events.Count) return;
        if (control.m_BgImg != null) control.m_BgImg.color = events[index].bgColor;
        index++;
    }
}

[HarmonyPatch(typeof(DialogSubControl), nameof(DialogSubControl.PlayDialogAnim))]
internal static class CustomDialogueColor
{
    private static void Prefix(DialogSubControl __instance) => CustomRecords.Guard(() => CustomDialogue.Show(__instance));
}
