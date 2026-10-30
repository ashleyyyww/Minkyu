using MelonLoader;

[assembly: MelonInfo(typeof(CustomAlbums.Android.Mod), "CustomAlbums Android", "0.1.0", "CustomAlbums Android contributors")]
namespace CustomAlbums.Android;

public sealed class Mod : MelonMod
{
    public static bool DialogueEnabled { get; set; }
    public static string GetAlbumPath(string uid) => AlbumCatalog.Albums.FirstOrDefault(a => a.Uid == uid)?.Path;
    public static T ReadAlbum<T>(string uid, Func<CustomAlbums.Core.MdmArchive, T> reader)
    {
        var album = AlbumCatalog.Albums.FirstOrDefault(a => a.Uid == uid)
            ?? throw new FileNotFoundException("Custom album unavailable");
        lock (album.Archive)
        {
            album.CheckUnchanged();
            return reader(album.Archive);
        }
    }
    public static CustomAlbums.Core.ChartRecord GetPersonalBest(string uid, int difficulty) => CustomRecords.Get(uid, difficulty);
    public static string GetChartHash(string uid, int difficulty)
    {
        var album = AlbumCatalog.Albums.FirstOrDefault(a => a.Uid == uid);
        return album != null && album.Charts.TryGetValue(difficulty, out var chart) ? chart.Md5 : null;
    }

    internal static bool Ready { get; private set; }
    internal static MelonLogger.Instance Log { get; private set; }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        try
        {
            AlbumCatalog.Load();
            CustomRecords.Guard(CustomRecords.Initialize);
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
