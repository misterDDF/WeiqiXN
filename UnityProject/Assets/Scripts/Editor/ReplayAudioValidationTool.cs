using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using XNClient.ChessBoard;

public static class ReplayAudioValidationTool
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    private sealed class ReplaySample : IDisposable
    {
        public readonly GameObject root;
        public readonly SceneBase scene;
        public readonly SceneComponentReplay state;
        public readonly ReplaySystem replay;
        public readonly ReplayAudioSystem audio;

        public ReplaySample(int captureCount = 0)
        {
            root = new GameObject("Replay audio validation") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            RectGrid grid = root.AddComponent<RectGrid>();
            grid.gridSize = 9;
            scene = new SceneBase(null, SceneCreateParams.Default);
            var board = new SceneComponentChessBoard(scene) { chessBoardGrid = grid };
            var duel = new SceneComponentDuel(scene);
            state = new SceneComponentReplay(scene) { isReplayLoaded = true, replayBoardSize = 9, hideChart = true };
            scene.AddComponent(board);
            scene.AddComponent(duel);
            scene.AddComponent(state);
            SetField(board.GetStoneViewCache(), "isDestroyed", true);
            audio = new ReplayAudioSystem(scene);
            replay = new ReplaySystem(scene);
            SetField(replay, "compChessBoard", board);
            SetField(replay, "compDuel", duel);
            SetField(replay, "compReplay", state);
            SetField(replay, "replayAudio", audio);
            if (captureCount > 0) {
                AddInitial(PlayerFlag.Player2, 1, 1);
                AddInitial(PlayerFlag.Player1, 0, 1);
                AddInitial(PlayerFlag.Player1, 1, 0);
                AddInitial(PlayerFlag.Player1, 1, 2);
            }
            if (captureCount > 1) {
                AddInitial(PlayerFlag.Player2, 3, 1);
                AddInitial(PlayerFlag.Player1, 3, 0);
                AddInitial(PlayerFlag.Player1, 4, 1);
                AddInitial(PlayerFlag.Player1, 3, 2);
            }
            state.replayMoves.Add(new ReplayMoveState { playerFlag = PlayerFlag.Player1, coords = new RectCoordinates(2, 1) });
            state.replayMoves.Add(new ReplayMoveState { playerFlag = PlayerFlag.Player2, isPass = true });
            state.replayMoves.Add(new ReplayMoveState { playerFlag = PlayerFlag.Player1, coords = new RectCoordinates(6, 6) });
            replay.GoFirst();
        }

        private void AddInitial(PlayerFlag flag, int x, int z)
        {
            state.replayInitialStones.Add(new ReplayMoveState { playerFlag = flag, coords = new RectCoordinates(x, z) });
        }

        public void Dispose()
        {
            replay.OnDestroy();
            audio.OnDestroy();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [MenuItem("自定义功能/Editor/验证复盘落子音效")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) {
            throw new InvalidOperationException("Replay audio validation requires an idle Editor.");
        }

        var report = new List<string>();
        foreach (int captureCount in new[] { 0, 1, 2 }) {
            using (var sample = new ReplaySample(captureCount)) {
                sample.replay.GoNext();
                ExpectPending(sample.audio, captureCount, true, "mainline next", report);
                sample.replay.GoPrev();
                ExpectPending(sample.audio, 0, false, "back cancels pending", report);
                sample.replay.GoToReplayMove(1);
                ExpectPending(sample.audio, 0, false, "one-step chart jump stays silent", report);
                sample.replay.GoNext();
                ExpectPending(sample.audio, 0, false, "pass stays silent", report);
                sample.replay.GoNext();
                ExpectPending(sample.audio, 0, true, "next after pass", report);
                sample.replay.GoNext();
                ExpectPending(sample.audio, 0, true, "end no-op preserves accepted sound", report);
                sample.replay.GoRelative(-5);
                ExpectPending(sample.audio, 0, false, "five-step back stays silent", report);
                sample.replay.GoRelative(5);
                ExpectPending(sample.audio, 0, false, "five-step forward stays silent", report);
                sample.replay.GoFirst();
                sample.replay.EnterTryMode();
                if (!sample.replay.TryApplyTryMove(new RectCoordinates(2, 1))) throw new InvalidOperationException("Legal try move failed.");
                ExpectPending(sample.audio, captureCount, true, "manual try move", report);
                if (sample.replay.TryApplyTryMove(new RectCoordinates(2, 1))) throw new InvalidOperationException("Occupied try move accepted.");
                ExpectPending(sample.audio, captureCount, true, "illegal click does not create or cancel sound", report);
                sample.replay.GoPrev();
                ExpectPending(sample.audio, 0, false, "try undo cancels", report);
                sample.replay.GoNext();
                ExpectPending(sample.audio, captureCount, true, "try next", report);
                sample.replay.ExitTryMode();
                ExpectPending(sample.audio, 0, false, "exit try cancels", report);
                sample.replay.GoNext();
                sample.audio.OnDestroy();
                ExpectPending(sample.audio, 0, false, "scene audio destroy cancels", report);
            }
        }

        using (var sample = new ReplaySample()) {
            sample.state.isFreeLayout = true;
            sample.replay.TryApplyBoardMove(new RectCoordinates(2, 1));
            ExpectPending(sample.audio, 0, true, "free layout move", report);
            sample.replay.GoPrev();
            sample.replay.TryApplyBoardMove(new RectCoordinates(4, 4));
            if (sample.state.tryMoves.Count != 1) throw new InvalidOperationException("Try branch was not truncated.");
            ExpectPending(sample.audio, 0, true, "new try branch", report);
        }

        using (var sample = new ReplaySample()) {
            sample.replay.EnterTryMode();
            sample.state.hasAiAnalysisRender = true;
            sample.state.aiRecommendationVariations[11] = new List<ReplayAiVariationMove> {
                new ReplayAiVariationMove { playerFlag = PlayerFlag.Player2, coords = new RectCoordinates(4, 4) },
                new ReplayAiVariationMove { playerFlag = PlayerFlag.Player1, coords = new RectCoordinates(5, 5) },
            };
            sample.replay.TryApplyTryMove(new RectCoordinates(2, 1));
            if (sample.state.tryMoves.Count != 3) throw new InvalidOperationException("AI variation did not expand.");
            ExpectPending(sample.audio, 0, true, "AI variation schedules one feedback", report);
            sample.audio.CancelPendingSounds();
            sample.replay.GoRelative(-5);
            sample.replay.GoRelative(5);
            ExpectPending(sample.audio, 0, false, "try multi-step stays silent", report);
        }

        ValidatePlayback(report);
        Directory.CreateDirectory("Temp/WeiqiXN");
        File.WriteAllLines("Temp/WeiqiXN/replay_audio_validation.txt", report);
        Debug.Log("Replay audio validation passed: " + report.Count + " checks.");
    }

    private static void ExpectPending(ReplayAudioSystem audio, int captures, bool place, string scenario, List<string> report)
    {
        StoneMoveAudio sounds = (StoneMoveAudio)GetField(audio, "stoneAudio");
        float placeTime = (float)GetField(sounds, "pendingPlaceTime");
        float captureTime = (float)GetField(sounds, "pendingCaptureTime");
        if ((placeTime >= 0f) != place || (captureTime >= 0f) != (captures > 0) || (int)GetField(sounds, "pendingCaptureCount") != captures) {
            throw new InvalidOperationException(scenario + ": incorrect pending sounds.");
        }
        if (place && Mathf.Abs(placeTime - Time.unscaledTime - ChessStoneView.PlacementDropSeconds) > 0.02f) {
            throw new InvalidOperationException(scenario + ": sound is not aligned with placement.");
        }
        report.Add(scenario + " captures=" + captures + ": PASS");
    }

    private static void ValidatePlayback(List<string> report)
    {
        string[] fields = { "audioRoot", "bgmSource", "sfxSource", "voiceSource", "stonePlaceClips", "stoneSingleCaptureClip", "stoneMultiCaptureClip", "lastStonePlaceTime", "lastStoneCaptureTime", "lastStonePlaceClipIndex" };
        var originals = new Dictionary<string, object>();
        foreach (string name in fields) originals[name] = typeof(GameAudio).GetField(name, PrivateStatic).GetValue(null);
        var root = new GameObject("Stone audio validation") { hideFlags = HideFlags.HideAndDontSave };
        AudioSource source = root.AddComponent<AudioSource>();
        AudioClip place = LoadClip("StonePlace/StonePlace_Sabaki_00.mp3");
        AudioClip single = LoadClip("Capture/Capture_Single.mp3");
        AudioClip multi = LoadClip("Capture/Capture_Multi.mp3");
        try {
            SetStatic("audioRoot", root);
            SetStatic("bgmSource", root.AddComponent<AudioSource>());
            SetStatic("sfxSource", source);
            SetStatic("voiceSource", root.AddComponent<AudioSource>());
            SetStatic("stonePlaceClips", new[] { place });
            SetStatic("stoneSingleCaptureClip", single);
            SetStatic("stoneMultiCaptureClip", multi);
            foreach (int captures in new[] { 0, 1, 2 }) {
                SetStatic("lastStonePlaceTime", -999f);
                SetStatic("lastStoneCaptureTime", -999f);
                source.clip = null;
                var sounds = new StoneMoveAudio();
                sounds.ScheduleCapture(captures);
                sounds.SchedulePlace();
                sounds.Update();
                if (source.clip != null) throw new InvalidOperationException("Audio played before landing.");
                SetField(sounds, "pendingPlaceTime", Time.unscaledTime);
                if (captures > 0) SetField(sounds, "pendingCaptureTime", Time.unscaledTime);
                sounds.Update();
                AudioClip expected = captures == 0 ? place : captures == 1 ? single : multi;
                if (source.clip != expected) throw new InvalidOperationException("Wrong stone sound or capture priority.");
                if ((float)GetField(sounds, "pendingPlaceTime") >= 0f || (float)GetField(sounds, "pendingCaptureTime") >= 0f) throw new InvalidOperationException("Played sound is still pending.");
                source.clip = null;
                sounds.Update();
                if (source.clip != null) throw new InvalidOperationException("Stone sound repeated.");
                sounds.SchedulePlace();
                sounds.Cancel();
                sounds.Update();
                if (source.clip != null) throw new InvalidOperationException("Canceled stone sound played.");
                report.Add("Playback, delay, capture priority, consume once, cancel captures=" + captures + ": PASS");
            }
            var duel = new DuelAudioSystem(null);
            typeof(DuelAudioSystem).GetMethod("OnAfterCaptureChessFromBoard", PrivateInstance).Invoke(duel, new object[] { new OnAfterCaptureChessFromBoard(2) });
            typeof(DuelAudioSystem).GetMethod("OnAfterAddChessToBoard", PrivateInstance).Invoke(duel, new object[] { new OnAfterAddChessToBoard(PlayerFlag.Player1, new RectCoordinates(2, 1)) });
            StoneMoveAudio pending = (StoneMoveAudio)GetField(duel, "stoneAudio");
            if ((int)GetField(pending, "pendingCaptureCount") != 2 || (float)GetField(pending, "pendingPlaceTime") < 0f) throw new InvalidOperationException("Duel audio scheduling regressed.");
            duel.OnDestroy();
            if ((float)GetField(pending, "pendingPlaceTime") >= 0f) throw new InvalidOperationException("Duel audio destroy did not cancel.");
            report.Add("Existing duel placement/capture scheduling: PASS");
        }
        finally {
            UnityEngine.Object.DestroyImmediate(root);
            foreach (string name in fields) SetStatic(name, originals[name]);
        }
    }

    private static AudioClip LoadClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/" + path);
        if (clip == null) throw new InvalidOperationException("Missing audio clip: " + path);
        return clip;
    }

    private static object GetField(object target, string name) => target.GetType().GetField(name, PrivateInstance).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    private static void SetStatic(string name, object value) => typeof(GameAudio).GetField(name, PrivateStatic).SetValue(null, value);
}
