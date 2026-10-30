using CustomAlbums.Data;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.GameCore;
using Il2CppGameLogic;
using System.Globalization;

namespace CustomAlbums.Android;

internal static class StageAdapter
{
    public static StageInfo Load(AlbumRecord album, int difficulty)
    {
        if (!album.Charts.TryGetValue(difficulty, out var chart))
            throw new InvalidDataException("Missing custom difficulty " + difficulty);
        var header = chart.Headers;
        var bms = new Bms
        {
            Info = new BmsInfo
            {
                Name = album.AssetPrefix + "_map" + difficulty,
                Genre = header.TryGetValue("GENRE", out var scene) ? scene : album.Metadata.Scene,
                Bpm = (float)chart.BaseBpm,
                Player = header.TryGetValue("PLAYER", out var player) && int.TryParse(player, out var speed) ? speed : 1
            },
            Notes = chart.Events.Select(e => new RawNote((float)e.Seconds, e.Value, e.Channel)).OrderBy(e => e.Time).ThenBy(e => e.Tone == "15" ? 0 : 1).ToList()
        };
        var stage = BmsLoader.TransmuteData(bms);
        stage.mapName = bms.Info.Name;
        stage.scene = bms.Info.Genre;
        stage.music = album.Uid;
        stage.difficulty = difficulty;
        stage.bpm = bms.Bpm;
        stage.md5 = chart.Md5;
        stage.sceneEvents = SceneEvents(chart);
        stage.name = album.Metadata.Name;
        CustomDialogue.Load(album, difficulty, stage);
        GlobalDataBase.dbStageInfo.SetStageInfo(stage);
        return stage;
    }

    private static Il2CppSystem.Collections.Generic.List<SceneEvent> SceneEvents(CustomAlbums.Core.BmsChart chart)
    {
        var events = new Il2CppSystem.Collections.Generic.List<SceneEvent>();
        foreach (var e in chart.Events)
        {
            if (e.Channel == "01")
                events.Add(new SceneEvent { time = (Il2CppSystem.Decimal)(float)e.Seconds, uid = "SceneEvent/" + e.Value });
            else if (e.Channel == "03" || e.Channel == "08")
            {
                var value = e.Channel == "03" ? int.Parse(e.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : chart.Headers["BPM" + e.Value];
                events.Add(new SceneEvent { time = (Il2CppSystem.Decimal)(float)e.Seconds, uid = "SceneEvent/OnBPMChanged", value = value });
            }
        }

        return events;
    }
}
