using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XNClient.ChessBoard;

public class DuelPageHudView
{
    private const float ActionNoticeHoldSeconds = 2.2f;
    private const float ActionNoticeFadeSeconds = 0.28f;

    private readonly DuelPageUI binder;
    private float actionNoticeHideStartTime = -1f;

    public bool IsOwnershipVisible { get; private set; }

    public DuelPageHudView(DuelPageUI binder)
    {
        this.binder = binder;
    }

    public void Reset()
    {
        SetSettingsPanelVisible(false);
        SetOwnershipActive(false);
        SetOwnershipResultPanelVisible(false);
        SetStoneRemovalCountdownVisible(false);
        SetGameEndResultPanelVisible(false);
        SetActionNoticeVisible(false);
    }

    public void Refresh(SceneBase mainScene)
    {
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            SetResignButtonVisible(false);
            return;
        }

        Player blackPlayer = mainScene.GetEntity<Player>(compDuel.player1Guid.value);
        Player whitePlayer = mainScene.GetEntity<Player>(compDuel.player2Guid.value);
        string curTurnPlayerGuid = compDuel.duelFSM.isActivated ? compDuel.curTurnPlayerGuid.value : string.Empty;

        RefreshPlayerInfoPanel(
            binder.txt_black_title,
            binder.txt_black_player_name,
            binder.txt_black_hold_time,
            binder.txt_black_byoyomi_count,
            binder.txt_black_byoyomi_time,
            compDuel,
            blackPlayer,
            curTurnPlayerGuid,
            MessageText.Get("duel_player_black")
        );
        RefreshPlayerInfoPanel(
            binder.txt_white_title,
            binder.txt_white_player_name,
            binder.txt_white_hold_time,
            binder.txt_white_byoyomi_count,
            binder.txt_white_byoyomi_time,
            compDuel,
            whitePlayer,
            curTurnPlayerGuid,
            MessageText.Get("duel_player_white")
        );

        RefreshGameEndResultPanel(mainScene, compDuel);
        RefreshSettingsActionVisibility(mainScene, compDuel);
    }

    public void OnDuelOwnershipResult(OnDuelOwnershipResult evt)
    {
        SetText(binder.txt_shape_lead_points, DuelOwnershipDisplayFormatter.BuildLeadText(evt.blackPoints, evt.whitePoints));
        SetText(binder.txt_shape_rule_info, BuildOwnershipRuleInfoText(evt.komi));
        SetOwnershipActive(true);
        SetOwnershipResultPanelVisible(evt == null || evt.showResultPanel);
    }

    public void ClearOwnership()
    {
        SetOwnershipActive(false);
        SetOwnershipResultPanelVisible(false);
    }

    public void BeginOwnershipRequest()
    {
        SetOwnershipActive(true);
        SetOwnershipResultPanelVisible(false);
        SetText(binder.txt_shape_lead_points, MessageText.Get("duel_ownership_lead_calculating"));
        SetText(binder.txt_shape_rule_info, MessageText.Get("duel_ownership_rule_calculating"));
    }

    public void OnDuelPassAccepted(OnDuelPassAccepted evt)
    {
        if (evt.consecutivePassCount >= 2 && !(Global.Instance.sceneManager.mainScene is OgsDuelScene)) {
            ShowActionNotice(MessageText.Get("duel_pass_consecutive_scoring"));
            return;
        }

        string playerText = GetPlayerFlagText(evt.playerFlag);
        string aiText = evt.isAiPlayer ? MessageText.Get("duel_ai_suffix") : string.Empty;
        ShowActionNotice(MessageText.Format("duel_pass_notice", playerText, aiText));
    }

    public void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        string playerText = GetPlayerFlagText(evt.playerFlag);
        string aiText = DuelPageInteractionState.IsAiPlayer(Global.Instance.sceneManager.mainScene, evt.playerFlag)
            ? MessageText.Get("duel_ai_suffix")
            : string.Empty;
        ShowActionNotice(MessageText.Format("duel_move_notice", playerText, aiText, FormatBoardPoint(evt.coords)));
    }

    public string BuildScoreConfirmContent(DuelScoreResult scoreResult)
    {
        string winnerText;
        if (scoreResult.winnerFlag == PlayerFlag.Player1) {
            winnerText = MessageText.Format("duel_score_black_win", FormatPointCount(scoreResult.margin));
        } else if (scoreResult.winnerFlag == PlayerFlag.Player2) {
            winnerText = MessageText.Format("duel_score_white_win", FormatPointCount(scoreResult.margin));
        } else {
            winnerText = MessageText.Get("duel_score_draw");
        }

        return MessageText.Format(
            GetScoreConfirmContentMessageKey(),
            FormatPointCount(scoreResult.blackScore),
            FormatPointCount(scoreResult.whiteScore),
            FormatPointCount(scoreResult.komi),
            winnerText);
    }

    private string BuildOwnershipRuleInfoText(float komi)
    {
        return DuelOwnershipDisplayFormatter.BuildRuleInfoText(komi, GetOwnershipHandicapCount(), IsOwnershipSen());
    }

    private int GetOwnershipHandicapCount()
    {
        SceneComponentOgsDuel compOgsDuel = Global.Instance.sceneManager.mainScene?.GetComponent<SceneComponentOgsDuel>();
        if (compOgsDuel != null) {
            return GetOgsOwnershipHandicapCount(compOgsDuel);
        }

        return DuelHandicapPlacement.GetHandicapCount(GetCurrentHandicapCfgId());
    }

    private bool IsOwnershipSen()
    {
        SceneComponentOgsDuel compOgsDuel = Global.Instance.sceneManager.mainScene?.GetComponent<SceneComponentOgsDuel>();
        if (compOgsDuel != null) {
            return GetOgsOwnershipHandicapCount(compOgsDuel) <= 0 &&
                compOgsDuel.hasKomi &&
                Mathf.Approximately(compOgsDuel.komi, 0.5f);
        }

        return DuelHandicapPlacement.IsSen(GetCurrentHandicapCfgId());
    }

    private int GetOgsOwnershipHandicapCount(SceneComponentOgsDuel compOgsDuel)
    {
        if (compOgsDuel == null) {
            return 0;
        }

        return Mathf.Max(
            Mathf.Max(compOgsDuel.ogsHandicapCount, compOgsDuel.initialStoneCount),
            compOgsDuel.openingSameColorMoveCount);
    }

    private string GetScoreConfirmContentMessageKey()
    {
        string handicapCfgId = GetCurrentHandicapCfgId();
        if (DuelHandicapPlacement.IsSen(handicapCfgId)) {
            return "duel_score_confirm_content_sen";
        }

        if (DuelHandicapPlacement.HasHandicap(handicapCfgId)) {
            return "duel_score_confirm_content_handicap";
        }

        return "duel_score_confirm_content";
    }

    private string GetCurrentHandicapCfgId()
    {
        SceneComponentDuel compDuel = Global.Instance.sceneManager.mainScene?.GetComponent<SceneComponentDuel>();
        return compDuel?.handicapCfgId.value ?? string.Empty;
    }

    public void OpenSettingsPanel()
    {
        SetSettingsPanelVisible(true);
    }

    public void CloseSettingsPanel()
    {
        SetSettingsPanelVisible(false);
    }

    public bool IsSettingsPanelVisible()
    {
        return binder.panel_duel_settings != null && binder.panel_duel_settings.activeSelf;
    }

    public void ShowActionNotice(string message)
    {
        SetText(binder.txt_duel_action_notice, message);
        SetActionNoticeVisible(true);
        if (binder.canvas_duel_action_notice != null) {
            binder.canvas_duel_action_notice.alpha = 1f;
        }
        actionNoticeHideStartTime = Time.unscaledTime + ActionNoticeHoldSeconds;
    }

    public void RefreshActionNotice()
    {
        if (binder.panel_duel_action_notice == null || !binder.panel_duel_action_notice.activeSelf || actionNoticeHideStartTime < 0f) {
            return;
        }

        float fadeElapsed = Time.unscaledTime - actionNoticeHideStartTime;
        if (fadeElapsed < 0f) {
            return;
        }

        float fadeProgress = ActionNoticeFadeSeconds <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / ActionNoticeFadeSeconds);
        if (binder.canvas_duel_action_notice != null) {
            binder.canvas_duel_action_notice.alpha = 1f - fadeProgress;
        }

        if (fadeProgress >= 1f) {
            SetActionNoticeVisible(false);
        }
    }

    public string GetPlayerDisplayName(Player player, SceneComponentDuel compDuel, string playerGuid)
    {
        if (player != null) {
            return GetPlayerDisplayName(compDuel, (PlayerFlag)player.playerFlag.value);
        }

        if (compDuel != null && !string.IsNullOrEmpty(playerGuid)) {
            if (playerGuid == compDuel.player1Guid.value) {
                return GetPlayerDisplayName(compDuel, PlayerFlag.Player1);
            }
            if (playerGuid == compDuel.player2Guid.value) {
                return GetPlayerDisplayName(compDuel, PlayerFlag.Player2);
            }
        }

        return MessageText.Get("duel_player_current");
    }

    public void SetResignButtonVisible(bool isVisible)
    {
        if (binder.btn_settings_resign != null) {
            binder.btn_settings_resign.gameObject.SetActive(isVisible);
        }
    }

    private void RefreshPlayerInfoPanel(
        TextMeshProUGUI titleText,
        TextMeshProUGUI playerNameText,
        TextMeshProUGUI holdText,
        TextMeshProUGUI byoyomiCountText,
        TextMeshProUGUI byoyomiTimeText,
        SceneComponentDuel compDuel,
        Player player,
        string curTurnPlayerGuid,
        string title
    )
    {
        bool isCurTurnPlayer = player != null && player.guid == curTurnPlayerGuid;
        PlayerFlag playerFlag = player != null ? (PlayerFlag)player.playerFlag.value : 0;
        string turnText = isCurTurnPlayer ? MessageText.Get("duel_turn_suffix").Replace(" ", string.Empty) : string.Empty;
        SetText(titleText, $"{title}{turnText}");
        SetText(playerNameText, GetPlayerDisplayName(compDuel, playerFlag));

        ComponentDuelInfo compDuelInfo = player?.GetComponent<ComponentDuelInfo>();
        bool isByoyomiEnabled = DuelPageInteractionState.IsByoyomiEnabled(compDuel, compDuelInfo);
        SetTextVisible(byoyomiCountText, isByoyomiEnabled);
        SetTextVisible(byoyomiTimeText, isByoyomiEnabled);

        if (compDuelInfo == null) {
            SetText(holdText, MessageText.Get("duel_hold_time_empty"));
            if (isByoyomiEnabled) {
                SetText(byoyomiCountText, MessageText.Get("duel_byoyomi_count_empty"));
                SetText(byoyomiTimeText, MessageText.Get("duel_byoyomi_time_empty"));
            }
            return;
        }

        SetText(holdText, MessageText.Format("duel_hold_time", FormatSeconds(compDuelInfo.holdLeftSeconds.value, compDuelInfo.isInfiniteTime.value)));
        if (!isByoyomiEnabled) {
            return;
        }

        SetText(byoyomiCountText, MessageText.Format("duel_byoyomi_count", compDuelInfo.byoyomiLeftCount.value));
        SetText(byoyomiTimeText, MessageText.Format("duel_byoyomi_time", FormatSeconds(compDuelInfo.byoyomiLeftSeconds.value, false)));
    }

    private void RefreshSettingsActionVisibility(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        bool isOgsDuel = mainScene is OgsDuelScene;
        OgsDuelSystem ogsDuelSystem = isOgsDuel ? mainScene?.GetSystem<OgsDuelSystem>() : null;
        bool isOgsStoneRemoval = ogsDuelSystem != null && ogsDuelSystem.IsInStoneRemovalPhase();
        bool canSubmitMove = isOgsDuel
            ? ogsDuelSystem != null && ogsDuelSystem.GetInputState().CanSubmitMove
            : DuelInputAuthority.GetLocalState(mainScene, compDuel).CanSubmitMove;
        bool canSubmitStoneRemovalCommand = ogsDuelSystem != null && ogsDuelSystem.CanSubmitStoneRemovalCommand();
        bool isLanDuel = compDuel != null && compDuel.isLanDuel.value;
        bool isGameEnd = compDuel?.duelFSM?.curState != null && compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_GAME_END;
        bool canRequestScore = !isOgsDuel && compDuel != null && !compDuel.isScoring && !isGameEnd && canSubmitMove;
        bool canTakeBack = isOgsDuel
            ? ogsDuelSystem != null && ogsDuelSystem.CanSubmitTakeBack()
            : DuelPageInteractionState.CanTakeBack(mainScene, compDuel);
        bool canResign = isOgsDuel
            ? ogsDuelSystem != null && ogsDuelSystem.CanSubmitResign()
            : canSubmitMove && DuelPageInteractionState.CanResign(mainScene, compDuel);
        DuelAiRecommendationSystem aiRecommendationSystem = mainScene?.GetSystem<DuelAiRecommendationSystem>();
        bool showAiAnalysis = !isLanDuel && !isOgsDuel;
        bool hasAiAnalysisRender = aiRecommendationSystem != null && aiRecommendationSystem.HasAiAnalysisRender;
        bool canAiAnalysis = showAiAnalysis && aiRecommendationSystem != null &&
            (hasAiAnalysisRender || (!aiRecommendationSystem.IsAiAnalyzing && aiRecommendationSystem.IsAiAnalysisEnabled));
        if (binder.panel_duel_ai_analysis != null) {
            binder.panel_duel_ai_analysis.SetActive(showAiAnalysis);
        }
        SetButtonInteractable(binder.btn_duel_ai_analysis, canAiAnalysis);
        RefreshAiAnalysisButtonSelection(showAiAnalysis && hasAiAnalysisRender);
        if (isOgsStoneRemoval) {
            RefreshOgsStoneRemovalButtons(ogsDuelSystem, canSubmitStoneRemovalCommand);
        } else {
            SetStoneRemovalCountdownVisible(false);
            SetOwnershipActive(IsOwnershipVisible);
            SetButtonText(binder.btn_duel_pass, "虚手");
            SetButtonInteractable(binder.btn_duel_ownership, true);
            SetButtonInteractable(binder.btn_duel_pass, canSubmitMove);
        }
        SetButtonInteractable(binder.btn_settings_request_score, canRequestScore);
        SetButtonInteractable(binder.btn_settings_take_back, canTakeBack);
        SetResignButtonVisible(canResign);
    }

    private void RefreshGameEndResultPanel(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        if (compDuel == null || compDuel.duelFSM == null || compDuel.duelFSM.curState == null) {
            SetGameEndResultPanelVisible(false);
            return;
        }

        if (compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_GAME_END) {
            SetGameEndResultPanelVisible(false);
            return;
        }

        Player winner = mainScene.GetEntity<Player>(compDuel.winnerGuid.value);
        string winnerText = GetPlayerDisplayName(winner, compDuel, compDuel.winnerGuid.value);
        string reasonText = BuildGameEndReasonText(mainScene, compDuel);

        SetText(binder.txt_game_end_winner, string.IsNullOrEmpty(compDuel.winnerGuid.value)
            ? MessageText.Get("duel_score_draw")
            : MessageText.Format("duel_game_end_winner", winnerText));
        SetText(binder.txt_game_end_reason, reasonText);
        SetGameEndResultPanelVisible(true);
    }

    private string BuildGameEndReasonText(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        if (compDuel == null) {
            return string.Empty;
        }

        if (compDuel.gameEndReason.value == DuelGameEndReason.Timeout) {
            Player loser = mainScene.GetEntity<Player>(compDuel.timeoutLoserGuid.value);
            return MessageText.Format("duel_game_end_timeout", GetPlayerDisplayName(loser, compDuel, compDuel.timeoutLoserGuid.value));
        }

        if (compDuel.gameEndReason.value == DuelGameEndReason.Resign) {
            Player loser = mainScene.GetEntity<Player>(compDuel.resignLoserGuid.value);
            return MessageText.Format("duel_game_end_resign", GetPlayerDisplayName(loser, compDuel, compDuel.resignLoserGuid.value));
        }

        if (compDuel.gameEndReason.value == DuelGameEndReason.Score
            || compDuel.gameEndReason.value == DuelGameEndReason.ConsecutivePass) {
            return MessageText.Format("duel_game_end_score", FormatPointCount(compDuel.finalScoreMargin.value));
        }

        return MessageText.Get("duel_game_end_default");
    }

    private string FormatSeconds(int seconds, bool isInfinite)
    {
        if (isInfinite || seconds < 0) {
            return "--";
        }

        int safeSeconds = Mathf.Max(seconds, 0);
        int minutes = safeSeconds / 60;
        int remainSeconds = safeSeconds % 60;
        return $"{minutes:00}:{remainSeconds:00}";
    }

    private string FormatPointCount(float pointCount)
    {
        return DuelOwnershipDisplayFormatter.FormatPointCount(pointCount);
    }

    private string FormatBoardPoint(RectCoordinates coords)
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentChessBoard compChessBoard = mainScene?.GetComponent<SceneComponentChessBoard>();
        int boardSize = compChessBoard?.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : 19;
        try {
            return KataGoPositionJsonBuilder.ToKataGoPoint(coords, boardSize);
        }
        catch (System.Exception) {
            return coords?.ToString() ?? MessageText.Get("common_empty_value");
        }
    }

    private void SetSettingsPanelVisible(bool isVisible)
    {
        if (binder.panel_duel_settings != null) {
            binder.panel_duel_settings.SetActive(isVisible);
        }
    }

    private void SetOwnershipResultPanelVisible(bool isVisible)
    {
        if (binder.panel_duel_shape_result != null) {
            binder.panel_duel_shape_result.SetActive(isVisible);
        }
    }

    private void SetGameEndResultPanelVisible(bool isVisible)
    {
        if (binder.panel_game_end_result != null) {
            binder.panel_game_end_result.SetActive(isVisible);
        }
    }

    private void SetActionNoticeVisible(bool isVisible)
    {
        if (binder.panel_duel_action_notice != null) {
            binder.panel_duel_action_notice.SetActive(isVisible);
        }
        if (!isVisible) {
            actionNoticeHideStartTime = -1f;
        }
    }

    private void SetButtonInteractable(Button button, bool interactable)
    {
        if (button != null) {
            button.interactable = interactable;
        }
    }

    private void RefreshOgsStoneRemovalButtons(OgsDuelSystem ogsDuelSystem, bool canSubmitStoneRemovalCommand)
    {
        int countdownSeconds = ogsDuelSystem != null ? ogsDuelSystem.GetStoneRemovalCountdownSeconds() : -1;
        SceneComponentOgsDuel compOgsDuel = Global.Instance.sceneManager.mainScene?.GetComponent<SceneComponentOgsDuel>();
        bool waitingForOpponent = compOgsDuel != null && compOgsDuel.localRemovedStonesAccepted;
        SetText(binder.txt_duel_ownership_button, waitingForOpponent
            ? "等待对方确认"
            : "确认死子");
        SetStoneRemovalCountdownText(countdownSeconds >= 0 ? FormatSeconds(countdownSeconds, false) : "--:--");
        SetButtonText(binder.btn_duel_pass, "不接受");
        SetButtonInteractable(binder.btn_duel_ownership, canSubmitStoneRemovalCommand && !waitingForOpponent);
        SetButtonInteractable(binder.btn_duel_pass, canSubmitStoneRemovalCommand);
    }

    private void RefreshAiAnalysisButtonSelection(bool selected)
    {
        if (binder.btn_duel_ai_analysis == null || EventSystem.current == null) {
            return;
        }

        GameObject buttonGameObject = binder.btn_duel_ai_analysis.gameObject;
        if (selected) {
            if (EventSystem.current.currentSelectedGameObject != buttonGameObject) {
                binder.btn_duel_ai_analysis.Select();
            }
            return;
        }

        if (EventSystem.current.currentSelectedGameObject == buttonGameObject) {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void SetOwnershipActive(bool isActive)
    {
        IsOwnershipVisible = isActive;
        SetText(binder.txt_duel_ownership_button, isActive
            ? MessageText.Get("common_close")
            : MessageText.Get("duel_ownership_button"));
    }

    private void SetStoneRemovalCountdownText(string value)
    {
        SetText(binder.txt_duel_stone_removal_countdown, value);
        SetStoneRemovalCountdownVisible(true);
    }

    private void SetStoneRemovalCountdownVisible(bool isVisible)
    {
        if (binder.txt_duel_stone_removal_countdown != null) {
            binder.txt_duel_stone_removal_countdown.gameObject.SetActive(isVisible);
        }
    }

    private void SetButtonText(Button button, string value)
    {
        TextMeshProUGUI text = button != null
            ? button.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        SetText(text, value);
    }

    private void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value;
        }
    }

    private void SetTextVisible(TextMeshProUGUI text, bool isVisible)
    {
        if (text != null && text.gameObject.activeSelf != isVisible) {
            text.gameObject.SetActive(isVisible);
        }
    }

    private string GetPlayerFlagText(PlayerFlag playerFlag)
    {
        return playerFlag == PlayerFlag.Player1
            ? MessageText.Get("duel_player_black")
            : MessageText.Get("duel_player_white");
    }

    private string GetPlayerDisplayName(SceneComponentDuel compDuel, PlayerFlag playerFlag)
    {
        if (compDuel != null) {
            if (playerFlag == PlayerFlag.Player1 && !string.IsNullOrWhiteSpace(compDuel.player1DisplayName.value)) {
                return compDuel.player1DisplayName.value;
            }

            if (playerFlag == PlayerFlag.Player2 && !string.IsNullOrWhiteSpace(compDuel.player2DisplayName.value)) {
                return compDuel.player2DisplayName.value;
            }
        }

        return GetPlayerFlagText(playerFlag);
    }
}
