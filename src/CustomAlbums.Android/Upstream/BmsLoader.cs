using CustomAlbums.Data;
using CustomAlbums.Managers;
using CustomAlbums.Utilities;
using Il2Cpp;
using Il2CppAssets.Scripts.GameCore;
using Il2CppAssets.Scripts.GameCore.Managers;
using Il2CppAssets.Scripts.PeroTools.Commons;
using Il2CppFormulaBase;
using Il2CppGameLogic;
using Il2CppPeroPeroGames.GlobalDefines;
using Il2CppPeroTools2.Resources;
using Il2CppSpine.Unity;
using UnityEngine;
using static CustomAlbums.Data.BmsStates;
using Animation = Il2CppSpine.Animation;
using Decimal = Il2CppSystem.Decimal;
using MelonLoader;

namespace CustomAlbums;

internal static class BmsLoader
{
    private static readonly Dictionary<string, NoteConfigData> NoteData = new();
    private static readonly Dictionary<string, Dictionary<string, NoteConfigData>> BossData = new();
    private static Decimal _delay;
    private static readonly MelonLogger.Instance Logger = new(nameof(BmsLoader));
    private static readonly Dictionary<string, float> StartDelayCache = new();
    private static readonly Dictionary<(string scene, string animation), float> AnimationDurationCache = new();
    internal static StageInfo TransmuteData(Bms bms)
    {
        if (NoteData.Count == 0)
            InitNoteData();
        MusicDataManager.Clear();
        _delay = 0;
        var noteData = bms.GetNoteData();
        LoadMusicData(noteData);
        MusicDataManager.Sort();
        ProcessBossData(bms);
        ProcessDelay(bms);
        MusicDataManager.Sort();
        ProcessGeminis();
        foreach (var mData in MusicDataManager.Data)
        {
            mData.tick -= _delay;
            mData.showTick = Decimal.Round(mData.tick - mData.dt, 2);
            if (mData.isLongPressType)
                mData.endIndex -= (int)(_delay / (Decimal)0.001f);
        }

        var stageInfo = ScriptableObject.CreateInstance<StageInfo>();
        stageInfo.musicDatas = new Il2CppSystem.Collections.Generic.List<MusicData>();
        foreach (var musicData in MusicDataManager.Data)
            CustomAlbums.Android.NativeValueList.AddChecked(stageInfo.musicDatas, musicData);
        stageInfo.delay = _delay;
        MusicDataManager.Clear();
        return stageInfo;
    }

    private static void InitNoteData()
    {
        foreach (var nData in SingletonScriptableObject<NoteDataMananger>.instance.noteDatas)
        {
            NoteData.TryAdd(nData.uid, nData);
            NoteData.TryAdd(Bms.GetNoteDataKey(nData.ibms_id, nData.pathway, nData.speed, nData.scene), nData);
            if (nData.GetNoteType() != NoteType.None || string.IsNullOrEmpty(nData.boss_action) || nData.boss_action == "0")
                continue;
            BossData.TryAdd(nData.scene, new Dictionary<string, NoteConfigData>());
            BossData[nData.scene].TryAdd(nData.boss_action, nData);
        }
    }

    private static void LoadMusicData(List<ProcessedNote> noteData)
    {
        short noteId = 1;
        foreach (var note in noteData)
        {
            if (noteId == short.MaxValue)
            {
                Logger.Warning($"Cannot process full chart, there are too many objects. Max objects is {short.MaxValue}.");
                break;
            }

            var configData = note.ToMusicConfigData();
            if (configData.time < 0)
                continue;
            var newNote = Interop.CreateTypeValue<MusicData>();
            newNote.objId = noteId++;
            newNote.tick = Decimal.Round(configData.time, 3);
            newNote.configData = configData;
            newNote.isLongPressEnd = false;
            newNote.isLongPressing = false;
            if (NoteData.TryGetValue(newNote.configData.note_uid, out var newNoteData))
                newNote.noteData = newNoteData;
            MusicDataManager.Add(newNote);
            if (!newNote.isLongPressStart)
                continue;
            var endIndex = (int)(Decimal.Round(newNote.tick + newNote.configData.length - newNote.noteData.left_great_range - newNote.noteData.left_perfect_range, 3) / (Decimal)0.001f);
            for (var i = 1; i <= newNote.longPressCount; i++)
            {
                var holdTick = Interop.CreateTypeValue<MusicData>();
                holdTick.objId = noteId++;
                holdTick.tick = i == newNote.longPressCount ? newNote.tick + newNote.configData.length : newNote.tick + (Decimal)0.1f * i;
                holdTick.configData = newNote.configData;
                var dataCopy = holdTick.configData;
                dataCopy.length = 0;
                holdTick.configData = dataCopy;
                holdTick.isLongPressing = i != newNote.longPressCount;
                holdTick.isLongPressEnd = i == newNote.longPressCount;
                holdTick.noteData = newNote.noteData;
                holdTick.longPressPTick = newNote.configData.time;
                holdTick.endIndex = endIndex;
                MusicDataManager.Add(holdTick);
            }
        }

    }

    private static void ProcessBossData(Bms bms)
    {
        var scene = bms.Info.Genre;
        var bossData = MusicDataManager.Data.Where(mData => mData.isBossNote).ToList();
        if (bossData.Count == 0)
            return;
        var finalData = bossData[^1];
        if (AnimStatesRight[finalData.noteData.boss_action] != BossState.OffScreen)
        {
            var finalNote = MusicDataManager.Data[^1];
            var tick = finalNote.tick;
            if (finalNote.isBossNote)
            {
                var startDelay = Decimal.Round((Decimal)GetStartDelay(finalNote.noteData.prefab_name), 3);
                var duration = (Decimal)GetAnimationDuration(scene, finalNote.noteData.boss_action);
                tick += Decimal.Round(-startDelay + duration, 3);
            }

            tick = Decimal.Round(tick + (Decimal)0.1f, 3);
            var exitNoteData = BossData[scene]["out"];
            var exitConfig = Interop.CreateTypeValue<MusicConfigData>();
            exitConfig.note_uid = exitNoteData.uid;
            exitConfig.time = tick;
            var exitMusicData = Interop.CreateTypeValue<MusicData>();
            exitMusicData.objId = 0;
            exitMusicData.tick = exitConfig.time;
            exitMusicData.configData = exitConfig;
            exitMusicData.noteData = exitNoteData;
            MusicDataManager.Add(exitMusicData);
            bossData.Add(exitMusicData);
        }

        NoteConfigData cachedGearConfig = null;
        var allNoteData = SingletonScriptableObject<NoteDataMananger>.instance.noteDatas;
        for (var i = 0; i < bossData.Count; i++)
        {
            var data = bossData[i];
            if (data.noteData.GetNoteType() != NoteType.Block)
                continue;
            MusicData bossAnimAhead = null;
            for (var j = i + 1; j < bossData.Count; j++)
            {
                var dataAhead = bossData[j];
                if (dataAhead.noteData.GetNoteType() == NoteType.Block)
                    continue;
                bossAnimAhead = dataAhead;
                break;
            }

            var bossAnimBefore = i > 0 ? bossData[i - 1] : null;
            var aheadTime = bossAnimAhead?.configData.time ?? Decimal.MinValue;
            var beforeTime = bossAnimBefore?.configData.time ?? Decimal.MinValue;
            var diffToAhead = Math.Abs((float)data.configData.time - (float)aheadTime);
            var diffToBefore = Math.Abs((float)data.configData.time - (float)beforeTime);
            var ahead = diffToAhead < diffToBefore;
            var stateBehind = bossAnimBefore != null ? AnimStatesRight[bossAnimBefore.noteData.boss_action] : BossState.OffScreen;
            var stateAhead = BossState.OffScreen;
            if (bossAnimAhead != null && AnimStatesLeft.TryGetValue(bossAnimAhead.noteData.boss_action, out var parsedState))
                stateAhead = parsedState;
            var usedState = ahead ? stateAhead : stateBehind;
            var correctState = usedState is BossState.Phase1 or BossState.Phase2;
            if (!correctState)
            {
                ahead = !ahead;
                usedState = ahead ? stateAhead : stateBehind;
                correctState = usedState is BossState.Phase1 or BossState.Phase2;
            }

            if (!correctState)
                continue;
            if ((ahead && AnimStatesLeft[data.noteData.boss_action] == usedState) || (!ahead && AnimStatesRight[data.noteData.boss_action] == usedState))
                continue;
            var phase = usedState == BossState.Phase1 ? 1 : 2;
            if (cachedGearConfig == null || cachedGearConfig.ibms_id != data.noteData.ibms_id || cachedGearConfig.pathway != data.noteData.pathway || cachedGearConfig.scene != data.noteData.scene || cachedGearConfig.speed != data.noteData.speed || !cachedGearConfig.boss_action.StartsWith($"boss_far_atk_{phase}"))
                foreach (var d in allNoteData)
                {
                    if (d.ibms_id != data.noteData.ibms_id || d.pathway != data.noteData.pathway || d.scene != data.noteData.scene || d.speed != data.noteData.speed || !d.boss_action.StartsWith($"boss_far_atk_{phase}"))
                        continue;
                    cachedGearConfig = d;
                    break;
                }

            if (cachedGearConfig == null)
                continue;
            var phaseGearConfig = cachedGearConfig;
            var fixedConfigData = Interop.CreateTypeValue<MusicConfigData>();
            fixedConfigData.blood = data.configData.blood;
            fixedConfigData.id = data.configData.id;
            fixedConfigData.length = data.configData.length;
            fixedConfigData.note_uid = phaseGearConfig.uid;
            fixedConfigData.pathway = data.configData.pathway;
            fixedConfigData.time = data.configData.time;
            var fixedGear = Interop.CreateTypeValue<MusicData>();
            fixedGear.objId = data.objId;
            fixedGear.tick = data.tick;
            fixedGear.configData = fixedConfigData;
            fixedGear.isLongPressEnd = data.isLongPressEnd;
            fixedGear.isLongPressing = data.isLongPressing;
            fixedGear.noteData = phaseGearConfig;
            MusicDataManager.Set(fixedGear.objId, fixedGear);
            bossData[i] = fixedGear;
        }

        var bossState = BossState.OffScreen;
        for (var i = 0; i < bossData.Count; i++)
        {
            var anim = bossData[i].noteData.boss_action;
            var nextState = AnimStatesLeft[anim];
            if (bossState != nextState)
            {
                var transfer = StateTransferAnims[bossState][nextState];
                var transferNoteData = BossData[scene][transfer];
                var alignment = TransferAlignment[transfer];
                var alignData = alignment == AnimAlignment.Right ? bossData[i] : bossData[i - 1];
                var rightDelay = (Decimal)GetStartDelay(bossData[i].noteData.prefab_name);
                var leftDelay = i == 0 ? 0 : (Decimal)GetStartDelay(bossData[i - 1].noteData.prefab_name);
                var alignDelay = alignment == AnimAlignment.Left ? leftDelay : rightDelay;
                var duration = (Decimal)GetAnimationDuration(scene, transfer);
                var mConfig = Interop.CreateTypeValue<MusicConfigData>();
                mConfig.note_uid = transferNoteData.uid;
                mConfig.time = Decimal.Round(alignData.tick - alignDelay - duration * (int)alignment, 3);
                var mData = Interop.CreateTypeValue<MusicData>();
                mData.tick = mConfig.time;
                mData.configData = mConfig;
                mData.noteData = transferNoteData;
                var tolerance = (Decimal)0.300f;
                var fits = alignment switch
                {
                    AnimAlignment.Left => mData.tick + duration < bossData[i].tick - rightDelay + tolerance,
                    _ => i == 0 || mData.tick > bossData[i - 1].tick - leftDelay
                };
                if (!fits)
                {
                    bossState = AnimStatesRight[bossData[i].noteData.boss_action];
                    continue;
                }

                MusicDataManager.Add(mData);
                bossData.Insert(i, mData);
                i--;
            }
            else
            {
                bossState = AnimStatesRight[bossData[i].noteData.boss_action];
            }
        }

    }

    private static void ProcessDelay(Bms bms)
    {
        var scene = bms.Info.Genre;
        var sceneIndex = int.Parse(scene.Split('_')[1], System.Globalization.CultureInfo.InvariantCulture);
        var sceneInfo = Singleton<StageBattleComponent>.instance.sceneInfo;
        var delayCache = new Dictionary<string, Decimal>();
        for (var i = 0; i < MusicDataManager.Data.Count; i++)
        {
            var mData = MusicDataManager.Data[i];
            if (!string.IsNullOrEmpty(mData.noteData.ibms_id))
            {
                var type = mData.noteData.GetNoteType();
                if (type == NoteType.SceneChange)
                    sceneIndex = sceneInfo[mData.noteData.ibms_id];
                var prefabName = mData.noteData.prefab_name;
                if (!string.IsNullOrEmpty(prefabName))
                {
                    if (type != NoteType.Hp && type != NoteType.Music)
                    {
                        var prefix = prefabName[..2];
                        if (prefix is not "00" and not "em" and not "bo")
                            prefabName = prefabName.Remove(0, 2).Insert(0, $"{sceneIndex:D2}");
                    }

                    if (!delayCache.ContainsKey(prefabName))
                    {
                        var gameObject = ResourcesManager.instance.LoadFromName<GameObject>(prefabName);
                        if (gameObject != null)
                        {
                            var spineActionController = gameObject.GetComponent<SpineActionController>();
                            delayCache[prefabName] = (Decimal)spineActionController.startDelay;
                        }
                    }

                    if (delayCache.TryGetValue(prefabName, out var delay))
                    {
                        mData.dt = delay;
                        MusicDataManager.Set(i, mData);
                    }
                }
            }

            var showTick = mData.tick - mData.dt;
            _delay = showTick < _delay ? showTick : _delay;
        }

        _delay = Decimal.Round(_delay, 3);
    }

    private static void ProcessGeminis()
    {
        var geminiCache = new Dictionary<Decimal, List<MusicData>>();
        for (var i = 1; i < MusicDataManager.Data.Count; i++)
        {
            var mData = MusicDataManager.Data[i];
            mData.doubleIdx = -1;
            MusicDataManager.Set(i, mData);
            if (mData.noteData.GetNoteType() != NoteType.Monster && mData.noteData.GetNoteType() != NoteType.Hide)
                continue;
            if (geminiCache.TryGetValue(mData.tick, out var geminiList))
            {
                var isNoteGemini = Bms.BmsIds[mData.noteData.ibms_id] == Bms.BmsId.Gemini;
                var isTargetGemini = false;
                var target = Interop.CreateTypeValue<MusicData>();
                foreach (var gemini in geminiList.Where(gemini => mData.isAir != gemini.isAir))
                {
                    target = gemini;
                    isTargetGemini = Bms.BmsIds[gemini.noteData.ibms_id] == Bms.BmsId.Gemini;
                    if (isNoteGemini && isTargetGemini)
                        break;
                    if (!isNoteGemini)
                        break;
                }

                if (target.objId > 0)
                {
                    mData.isDouble = isNoteGemini && isTargetGemini;
                    mData.doubleIdx = target.objId;
                    target.isDouble = isNoteGemini && isTargetGemini;
                    target.doubleIdx = mData.objId;
                    MusicDataManager.Set(mData.objId, mData);
                    MusicDataManager.Set(target.objId, target);
                }
            }
            else
            {
                geminiCache[mData.tick] = new List<MusicData>();
            }

            geminiCache[mData.tick].Add(mData);
        }

    }

    private static float GetStartDelay(string prefab)
    {
        if (StartDelayCache.TryGetValue(prefab, out var cached))
            return cached;
        var delay = ResourcesManager.instance.LoadFromName<GameObject>(prefab).GetComponent<SpineActionController>().startDelay;
        StartDelayCache[prefab] = delay;
        return delay;
    }

    private static float GetAnimationDuration(string scene, string animation)
    {
        var cacheKey = (scene, animation);
        if (AnimationDurationCache.TryGetValue(cacheKey, out var cached))
            return cached;
        var controller = ResourcesManager.instance.LoadFromName<GameObject>(Boss.Instance.BossFestival($"{scene.Split("_")[1]}01_boss")).GetComponent<SpineActionController>();
        var animations = controller.gameObject.GetComponent<SkeletonAnimation>().skeletonDataAsset.GetSkeletonData(true).Animations;
        var animName = animation;
        foreach (var actionData in controller.actionData)
        {
            if (actionData.name != animation)
                continue;
            if (actionData.actionIdx is { Length: > 0 })
                animName = actionData.actionIdx[0];
            break;
        }

        var duration = animations.Find((Il2CppSystem.Predicate<Animation>)((Animation a) => a.Name == animName)).Duration;
        AnimationDurationCache[cacheKey] = duration;
        return duration;
    }
}
