using MelonLoader;

[assembly: MelonInfo(typeof(CustomAlbums.Android.Mod), "CustomAlbums Android", "0.1.0", "CustomAlbums Android contributors")]
namespace CustomAlbums.Android;

public sealed class Mod : MelonMod
{
    internal static bool Ready { get; private set; }
    internal static MelonLogger.Instance Log { get; private set; }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        try
        {
            AlbumCatalog.Load();
            ResourceHook.Attach();
            AchievementIsolationHook.Attach();
            Ready = true;
            LoggerInstance.Msg("CA_ANDROID_INITIALIZED");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error("CA_INITIALIZATION_FAILED " + ex);
        }
    }
}
