using TMPro;
using UnityEngine;
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
            binder.txt_black_hold_time,
            binder.txt_black_byoyomi_count,
            binder.txt_black_byoyomi_time,
            compDuel,
            blackPlayer,
            curTurnPlayerGuid,
            "黑方"
        );
        RefreshPlayerInfoPanel(
            binder.txt_white_title,
            binder.txt_white_hold_time,
            binder.txt_white_byoyomi_count,
            binder.txt_white_byoyomi_time,
            compDuel,
            whitePlayer,
            curTurnPlayerGuid,
            "白方"
        );

        RefreshGameEndResultPanel(mainScene, compDuel);
        RefreshSettingsActionVisibility(mainScene, compDuel);
    }

    public void OnDuelOwnershipResult(OnDuelOwnershipResult evt)
    {
        SetText(binder.txt_ownership_black_points, $"黑方目数: {FormatPointCount(evt.blackPoints)}");
        SetText(binder.txt_ownership_white_points, $"白方目数: {FormatPointCount(evt.whitePoints)}（贴目后）");
        SetOwnershipActive(true);
        SetOwnershipResultPanelVisible(true);
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
        SetText(binder.txt_ownership_black_points, "黑方目数: 计算中...");
        SetText(binder.txt_ownership_white_points, "白方目数: 计算中...");
    }

    public void OnDuelPassAccepted(OnDuelPassAccepted evt)
    {
        if (evt.consecutivePassCount >= 2) {
            ShowActionNotice("双方连续虚手，正在数子...");
            return;
        }

        string playerText = evt.playerFlag == PlayerFlag.Player1 ? "黑方" : "白方";
        string aiText = evt.isAiPlayer ? "（AI）" : string.Empty;
        ShowActionNotice($"{playerText}{aiText}虚手");
    }

    public void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        string playerText = evt.playerFlag == PlayerFlag.Player1 ? "黑方" : "白方";
        string aiText = DuelPageInteractionState.IsAiPlayer(Global.Instance.sceneManager.mainScene, evt.playerFlag) ? "（AI）" : string.Empty;
        ShowActionNotice($"{playerText}{aiText}落子 {FormatBoardPoint(evt.coords)}");
    }

    public string BuildScoreConfirmContent(DuelScoreResult scoreResult)
    {
        string winnerText;
        if (scoreResult.winnerFlag == PlayerFlag.Player1) {
            winnerText = $"黑方胜 {FormatPointCount(scoreResult.margin)} 目";
        } else if (scoreResult.winnerFlag == PlayerFlag.Player2) {
            winnerText = $"白方胜 {FormatPointCount(scoreResult.margin)} 目";
        } else {
            winnerText = "双方和棋";
        }

        return $"黑方: {FormatPointCount(scoreResult.blackScore)} 目\n白方: {FormatPointCount(scoreResult.whiteScore)} 目（含贴目 {FormatPointCount(scoreResult.komi)}）\n结果: {winnerText}";
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
            return (PlayerFlag)player.playerFlag.value == PlayerFlag.Player1 ? "黑方" : "白方";
        }

        if (compDuel != null && !string.IsNullOrEmpty(playerGuid)) {
            if (playerGuid == compDuel.player1Guid.value) {
                return "黑方";
            }
            if (playerGuid == compDuel.player2Guid.value) {
                return "白方";
            }
        }

        return "当前方";
    }

    public void SetResignButtonVisible(bool isVisible)
    {
        if (binder.btn_settings_resign != null) {
            binder.btn_settings_resign.gameObject.SetActive(isVisible);
        }
    }

    private void RefreshPlayerInfoPanel(
        TextMeshProUGUI titleText,
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
        bool isAi = DuelPageInteractionState.IsAiPlayer(player, compDuel);
        string playerTypeText = isAi ? "AI" : "人类";
        string turnText = isCurTurnPlayer ? " · 行棋中" : string.Empty;
        SetText(titleText, $"{title} · {playerTypeText}{turnText}");

        ComponentDuelInfo compDuelInfo = player?.GetComponent<ComponentDuelInfo>();
        bool isByoyomiEnabled = DuelPageInteractionState.IsByoyomiEnabled(compDuel, compDuelInfo);
        SetTextVisible(byoyomiCountText, isByoyomiEnabled);
        SetTextVisible(byoyomiTimeText, isByoyomiEnabled);

        if (compDuelInfo == null) {
            SetText(holdText, "主时间 --");
            if (isByoyomiEnabled) {
                SetText(byoyomiCountText, "剩余读秒 --");
                SetText(byoyomiTimeText, "读秒时间 --");
            }
            return;
        }

        SetText(holdText, $"主时间 {FormatSeconds(compDuelInfo.holdLeftSeconds.value, compDuelInfo.isInfiniteTime.value)}");
        if (!isByoyomiEnabled) {
            return;
        }

        SetText(byoyomiCountText, $"剩余读秒 {compDuelInfo.byoyomiLeftCount.value} 次");
        SetText(byoyomiTimeText, $"读秒时间 {FormatSeconds(compDuelInfo.byoyomiLeftSeconds.value, false)}");
    }

    private void RefreshSettingsActionVisibility(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        bool canSubmitMove = DuelInputAuthority.GetLocalState(mainScene, compDuel).CanSubmitMove;
        bool isLanDuel = compDuel != null && compDuel.isLanDuel.value;
        bool isGameEnd = compDuel?.duelFSM?.curState != null && compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_GAME_END;
        bool canRequestScore = compDuel != null && !isLanDuel && !compDuel.isScoring && !isGameEnd;
        bool canTakeBack = !isLanDuel && DuelPageInteractionState.CanTakeBack(compDuel);
        SetButtonInteractable(binder.btn_duel_pass, !isLanDuel && canSubmitMove);
        SetButtonInteractable(binder.btn_settings_request_score, canRequestScore);
        SetButtonInteractable(binder.btn_settings_take_back, canTakeBack);
        SetResignButtonVisible(canSubmitMove && DuelPageInteractionState.CanResign(mainScene, compDuel));
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

        SetText(binder.txt_game_end_winner, string.IsNullOrEmpty(compDuel.winnerGuid.value) ? "双方和棋" : $"{winnerText}胜出");
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
            return $"{GetPlayerDisplayName(loser, compDuel, compDuel.timeoutLoserGuid.value)}超时判负";
        }

        if (compDuel.gameEndReason.value == DuelGameEndReason.Resign) {
            Player loser = mainScene.GetEntity<Player>(compDuel.resignLoserGuid.value);
            return $"{GetPlayerDisplayName(loser, compDuel, compDuel.resignLoserGuid.value)}认输";
        }

        if (compDuel.gameEndReason.value == DuelGameEndReason.Score
            || compDuel.gameEndReason.value == DuelGameEndReason.ConsecutivePass) {
            return $"领先 {FormatPointCount(compDuel.finalScoreMargin.value)} 目";
        }

        return "对局结束";
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
        return Mathf.Approximately(pointCount, Mathf.Round(pointCount))
            ? Mathf.RoundToInt(pointCount).ToString()
            : pointCount.ToString("0.0");
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
            return coords?.ToString() ?? "--";
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
        if (binder.panel_duel_ownership_result != null) {
            binder.panel_duel_ownership_result.SetActive(isVisible);
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

    private void SetOwnershipActive(bool isActive)
    {
        IsOwnershipVisible = isActive;
        SetText(binder.txt_duel_ownership_button, isActive ? "关闭" : "形势");
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
}
