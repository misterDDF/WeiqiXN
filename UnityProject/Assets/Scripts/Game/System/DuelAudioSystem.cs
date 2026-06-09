using System;

public class DuelAudioSystem : SystemBase
{
    private const int CountdownEverySecondStart = 10;
    private const float VoiceMinIntervalSeconds = 0.65f;

    public override string systemName => GetSystemName<DuelAudioSystem>();

    private SceneComponentDuel compDuel;
    private SceneComponentOgsDuel compOgsDuel;
    private bool hasPlayedGameStarted;
    private bool hasPlayedGameEnd;
    private bool hasPlayedStoneRemoval;
    private bool wasInByoyomi;
    private bool wasInStoneRemoval;
    private string lastTurnPlayerGuid = string.Empty;
    private int lastAnnouncedCountdownSeconds = -1;
    private int lastAnnouncedByoyomiCount = -1;
    private float lastVoicePlayTime = -999f;
    private bool lastVoiceWasCountdown;

    public DuelAudioSystem(SceneBase scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        compDuel = scene.GetComponent<SceneComponentDuel>();
        compOgsDuel = scene.GetComponent<SceneComponentOgsDuel>();

        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnAfterCaptureChessFromBoard>(OnAfterCaptureChessFromBoard);
        scene.RegisterSystemEvent<OnDuelPassAccepted>(OnDuelPassAccepted);
        scene.RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);
        scene.RegisterSystemEvent<OnOgsStoneRemovalStateChanged>(OnOgsStoneRemovalStateChanged);
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
        RefreshTurnVoice();
        RefreshStoneRemovalVoice();
        RefreshGameEndVoice();
    }

    private void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        if (evt == null) {
            return;
        }

        GameAudio.PlayStonePlace();
    }

    private void OnAfterCaptureChessFromBoard(OnAfterCaptureChessFromBoard evt)
    {
        if (evt == null || evt.captureCount <= 0) {
            return;
        }

        GameAudio.PlayStoneCapture(evt.captureCount);
    }

    private void OnDuelPassAccepted(OnDuelPassAccepted evt)
    {
        if (evt == null) {
            return;
        }

        TryPlayDuelVoice(DuelVoiceCue.Pass);
    }

    private void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        if (evt == null) {
            return;
        }

        if (evt.curStateName == DuelStateDefine.STATE_TURN_INPUT) {
            PlayGameStartedOnce();
            ResetTurnVoiceState();
            return;
        }

        if (evt.curStateName == DuelStateDefine.STATE_GAME_END) {
            PlayGameEndVoiceOnce();
        }
    }

    private void OnOgsStoneRemovalStateChanged(OnOgsStoneRemovalStateChanged evt)
    {
        RefreshStoneRemovalVoice();
    }

    private void RefreshTurnVoice()
    {
        if (compDuel == null || !IsInTurnInput()) {
            return;
        }

        if (PlayGameStartedOnce()) {
            return;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        ComponentDuelInfo duelInfo = curPlayer?.GetComponent<ComponentDuelInfo>();
        if (duelInfo == null) {
            return;
        }

        if (lastTurnPlayerGuid != compDuel.curTurnPlayerGuid.value) {
            ResetTurnVoiceState();
        }

        if (!ShouldAnnounceForCurrentTurn(curPlayer) || duelInfo.isInfiniteTime.value) {
            return;
        }

        if (duelInfo.isInByoyomi.value && !wasInByoyomi) {
            if (PlayByoyomiStartVoice()) {
                wasInByoyomi = true;
                return;
            }
        }

        wasInByoyomi = duelInfo.isInByoyomi.value;
        if (duelInfo.isInByoyomi.value) {
            if (AnnounceByoyomiCount(duelInfo.byoyomiLeftCount.value)) {
                return;
            }
            AnnounceCountdown(duelInfo.byoyomiLeftSeconds.value);
        } else if (!HasByoyomiTime(duelInfo)) {
            AnnounceCountdown(duelInfo.holdLeftSeconds.value);
        }
    }

    private void RefreshStoneRemovalVoice()
    {
        bool isInStoneRemoval = IsInOgsStoneRemoval();
        if (isInStoneRemoval && !wasInStoneRemoval && !hasPlayedStoneRemoval) {
            hasPlayedStoneRemoval = true;
            TryPlayDuelVoice(DuelVoiceCue.RemoveDeadStones);
        }

        if (!isInStoneRemoval) {
            hasPlayedStoneRemoval = false;
        }
        wasInStoneRemoval = isInStoneRemoval;
    }

    private void RefreshGameEndVoice()
    {
        if (IsInGameEnd()) {
            PlayGameEndVoiceOnce();
        }
    }

    private bool PlayGameStartedOnce()
    {
        if (hasPlayedGameStarted || compDuel == null) {
            return false;
        }

        if (IsOgsScene() && (compOgsDuel == null || compOgsDuel.acceptedMoveCount < 0 || string.IsNullOrEmpty(compOgsDuel.phase))) {
            return false;
        }

        if (!TryPlayDuelVoice(DuelVoiceCue.GameStarted)) {
            return false;
        }

        hasPlayedGameStarted = true;
        return true;
    }

    private void PlayGameEndVoiceOnce()
    {
        if (hasPlayedGameEnd || compDuel == null) {
            return;
        }

        PlayerFlag winnerFlag = GetWinnerFlag();
        if (winnerFlag == PlayerFlag.Player1) {
            hasPlayedGameEnd = TryPlayDuelVoice(IsLocalPlayer(PlayerFlag.Player1) ? DuelVoiceCue.YouHaveWon : DuelVoiceCue.BlackWins);
        } else if (winnerFlag == PlayerFlag.Player2) {
            hasPlayedGameEnd = TryPlayDuelVoice(IsLocalPlayer(PlayerFlag.Player2) ? DuelVoiceCue.YouHaveWon : DuelVoiceCue.WhiteWins);
        } else {
            hasPlayedGameEnd = TryPlayDuelVoice(DuelVoiceCue.Tie);
        }
    }

    private bool PlayByoyomiStartVoice()
    {
        if (IsOgsScene()) {
            return TryPlayDuelVoice(DuelVoiceCue.StartCounting);
        }
        return TryPlayDuelVoice(DuelVoiceCue.Byoyomi);
    }

    private bool AnnounceByoyomiCount(int byoyomiLeftCount)
    {
        if (byoyomiLeftCount <= 0 || byoyomiLeftCount == lastAnnouncedByoyomiCount) {
            return false;
        }

        if (byoyomiLeftCount == 5) {
            return TryAnnounceByoyomiCount(byoyomiLeftCount, DuelVoiceCue.PeriodsLeft5);
        } else if (byoyomiLeftCount == 4) {
            return TryAnnounceByoyomiCount(byoyomiLeftCount, DuelVoiceCue.PeriodsLeft4);
        } else if (byoyomiLeftCount == 3) {
            return TryAnnounceByoyomiCount(byoyomiLeftCount, DuelVoiceCue.PeriodsLeft3);
        } else if (byoyomiLeftCount == 2) {
            return TryAnnounceByoyomiCount(byoyomiLeftCount, DuelVoiceCue.PeriodsLeft2);
        } else if (byoyomiLeftCount == 1) {
            return TryAnnounceByoyomiCount(byoyomiLeftCount, DuelVoiceCue.LastPeriod);
        }
        return false;
    }

    private bool TryAnnounceByoyomiCount(int byoyomiLeftCount, DuelVoiceCue cue)
    {
        if (!TryPlayDuelVoice(cue)) {
            return false;
        }

        lastAnnouncedByoyomiCount = byoyomiLeftCount;
        return true;
    }

    private bool HasByoyomiTime(ComponentDuelInfo duelInfo)
    {
        return duelInfo != null && (duelInfo.byoyomiLeftCount.value > 0 || duelInfo.byoyomiLeftSeconds.value > 0);
    }

    private void AnnounceCountdown(int secondsLeft)
    {
        if (secondsLeft <= 0 || secondsLeft > CountdownEverySecondStart || secondsLeft == lastAnnouncedCountdownSeconds) {
            return;
        }

        if (GameAudio.IsDuelVoicePlaying() || IsVoiceThrottled()) {
            lastAnnouncedCountdownSeconds = secondsLeft;
            return;
        }

        if (TryPlayDuelVoice(GetCountdownVoiceCue(secondsLeft), true)) {
            lastAnnouncedCountdownSeconds = secondsLeft;
        }
    }

    private void ResetTurnVoiceState()
    {
        lastTurnPlayerGuid = compDuel != null ? compDuel.curTurnPlayerGuid.value : string.Empty;
        lastAnnouncedCountdownSeconds = -1;
        lastAnnouncedByoyomiCount = -1;
        wasInByoyomi = false;
    }

    private bool IsInTurnInput()
    {
        if (compDuel == null || compDuel.duelFSM?.curState == null) {
            return IsOgsScene() && !IsInOgsStoneRemoval() && !IsOgsFinished();
        }

        return compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_TURN_INPUT;
    }

    private bool IsInGameEnd()
    {
        return compDuel?.duelFSM?.curState != null &&
            compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_GAME_END;
    }

    private bool ShouldAnnounceForCurrentTurn(Player curPlayer)
    {
        PlayerFlag currentFlag = curPlayer != null ? (PlayerFlag)curPlayer.playerFlag.value : 0;
        PlayerFlag localFlag = (PlayerFlag)(compDuel?.localPlayerFlag.value ?? 0);
        if (localFlag == PlayerFlag.Player1 || localFlag == PlayerFlag.Player2) {
            return currentFlag == localFlag;
        }

        return !compDuel.isAiDuel.value && !compDuel.isLanDuel.value && !IsOgsScene();
    }

    private bool IsLocalPlayer(PlayerFlag playerFlag)
    {
        return compDuel != null && (PlayerFlag)compDuel.localPlayerFlag.value == playerFlag;
    }

    private PlayerFlag GetWinnerFlag()
    {
        if (compDuel == null) {
            return 0;
        }

        if (compDuel.winnerGuid.value == compDuel.player1Guid.value) {
            return PlayerFlag.Player1;
        }
        if (compDuel.winnerGuid.value == compDuel.player2Guid.value) {
            return PlayerFlag.Player2;
        }
        return 0;
    }

    private bool IsOgsScene()
    {
        return compOgsDuel != null;
    }

    private bool IsInOgsStoneRemoval()
    {
        if (compOgsDuel == null) {
            return false;
        }

        string phase = compOgsDuel.phase ?? string.Empty;
        return string.Equals(phase, "stone removal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "stone_removal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "stone-removal", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsOgsFinished()
    {
        if (compOgsDuel == null) {
            return false;
        }

        string phase = compOgsDuel.phase ?? string.Empty;
        return string.Equals(phase, "finished", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "finished_game", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "ended", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "complete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "completed", StringComparison.OrdinalIgnoreCase);
    }

    private DuelVoiceCue GetCountdownVoiceCue(int secondsLeft)
    {
        switch (secondsLeft) {
            case 10:
                return DuelVoiceCue.Countdown10;
            case 9:
                return DuelVoiceCue.Countdown09;
            case 8:
                return DuelVoiceCue.Countdown08;
            case 7:
                return DuelVoiceCue.Countdown07;
            case 6:
                return DuelVoiceCue.Countdown06;
            case 5:
                return DuelVoiceCue.Countdown05;
            case 4:
                return DuelVoiceCue.Countdown04;
            case 3:
                return DuelVoiceCue.Countdown03;
            case 2:
                return DuelVoiceCue.Countdown02;
            default:
                return DuelVoiceCue.Countdown01;
        }
    }

    private bool TryPlayDuelVoice(DuelVoiceCue cue, bool isCountdown = false)
    {
        if (IsVoiceThrottled() && !lastVoiceWasCountdown) {
            return false;
        }

        lastVoicePlayTime = UnityEngine.Time.unscaledTime;
        lastVoiceWasCountdown = isCountdown;
        GameAudio.PlayDuelVoice(cue);
        return true;
    }

    private bool IsVoiceThrottled()
    {
        return UnityEngine.Time.unscaledTime - lastVoicePlayTime < VoiceMinIntervalSeconds;
    }
}
