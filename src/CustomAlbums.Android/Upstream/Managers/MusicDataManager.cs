using MelonLoader;
using System.Collections.ObjectModel;
using CustomAlbums.Utilities;
using Il2CppGameLogic;
using Decimal = Il2CppSystem.Decimal;

namespace CustomAlbums.Managers;

internal static class MusicDataManager
{
    private static readonly List<MusicData> MusicDataList = new();
    private static readonly MelonLogger.Instance Logger = new(nameof(MusicDataManager));
    static MusicDataManager()
    {
        MusicDataList.Add(Interop.CreateTypeValue<MusicData>());
    }

    public static ReadOnlyCollection<MusicData> Data => MusicDataList.AsReadOnly();

    public static void Add(MusicData data)
    {
        if (MusicDataList.Count >= short.MaxValue - 1)
        {
            Logger.Warning($"BMS is too large to load. Max MusicData allowed is {short.MaxValue}!");
            return;
        }

        MusicDataList.Add(data);
    }

    public static void Sort()
    {
        MusicDataList.RemoveAt(0);
        // Calculate each key once. The PC comparator crosses IL2CPP for every decimal
        // operation; thousands of notes make that very expensive on Android ARM64.
        var sorted = MusicDataList.Select(data => (Data: data, Tick: Managed(data.tick) - Managed(data.dt)))
            .OrderBy(item => item.Tick).Select(item => item.Data).ToArray();
        MusicDataList.Clear();
        MusicDataList.AddRange(sorted);
        MusicDataList.Insert(0, Interop.CreateTypeValue<MusicData>());
        for (var i = 1; i < MusicDataList.Count; i++)
        {
            var musicData = MusicDataList[i];
            musicData.tick = Decimal.Round(musicData.tick, 3);
            musicData.objId = (short)i;
            MusicDataList[i] = musicData;
        }

    }

    private static decimal Managed(Decimal value)
        => new(value.lo, value.mid, value.hi, value.flags < 0, (byte)((value.flags >> 16) & 0xff));

    public static void Set(int index, MusicData data)
    {
        MusicDataList[index] = data;
    }

    public static void Clear()
    {
        MusicDataList.Clear();
        MusicDataList.Add(Interop.CreateTypeValue<MusicData>());
    }
}
