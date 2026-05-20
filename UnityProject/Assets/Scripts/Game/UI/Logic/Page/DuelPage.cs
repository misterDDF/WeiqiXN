using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XNClient.ChessBoard;

public class DuelPage : UIPageWithBinder<DuelPageUI>
{
    public override string pageName => UIPage.GetPageName<DuelPage>();
    public GameObject aimChessPreview;
    public RectCoordinates aimCoords = new RectCoordinates(-1, -1);
    private PlayerFlag aimChessPreviewPlayerFlag;
    private bool isOwnershipVisible;
    private int pendingScorePopupRequestId;

    protected override void OnLoaded()
    {
        base.OnLoaded();

        RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);
        RegisterSystemEvent<OnDuelOwnershipResult>(OnDuelOwnershipResult);
        RegisterSystemEvent<OnClearDuelOwnership>(OnClearDuelOwnership);
        RegisterSystemEvent<OnDuelScoreResult>(OnDuelScoreResult);
        RegisterSystemEvent<OnDuelScoreFailed>(OnDuelScoreFailed);

        BindPrefabHud();
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        SetSettingsPanelVisible(false);
        SetOwnershipActive(false);
        SetOwnershipResultPanelVisible(false);
        SetGameEndResultPanelVisible(false);
        RefreshDuelHud();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        RefreshDuelHud();

        if (IsSettingsPanelVisible()) {
            aimCoords.SetValue(-1, -1);
            SetAimChessPreviewActive(false);
            return;
        }

        aimCoords.SetValue(-1, -1);
        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
        if (CanAcceptHumanTurnInput(mainScene, compDuel)) {
            RefreshAimChessPreview(mainScene, compDuel);
        } else {
            SetAimChessPreviewActive(false);
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.Mouse0) && !IsPointerOverUI()) {
            OnMouse0Down();
        }
    }

    protected override void OnClose()
    {
        base.OnClose();
        if (aimChessPreview != null) {
            GameObject.DestroyImmediate(aimChessPreview);
            aimChessPreview = null;
        }
    }

    private void RefreshAimChessPreview(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        Player curPlayer = mainScene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            SetAimChessPreviewActive(false);
            return;
        }

        Ray mouseRay = Global.Instance.uiManager.uiCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(mouseRay.origin, mouseRay.direction, out var hitInfo, 500)) {
            SetAimChessPreviewActive(false);
            return;
        }

        var compChessBoard = mainScene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard == null) {
            SetAimChessPreviewActive(false);
            return;
        }

        Transform gridTransform = compChessBoard.chessBoardGrid.transform;
        Vector3 localHitPoint = gridTransform.InverseTransformPoint(hitInfo.point);
        float cellSideLength = ChessBoardConfig.rectCellSideLength;

        int nearestCellX = Mathf.RoundToInt(localHitPoint.x / cellSideLength - 0.5f);
        int nearestCellZ = compChessBoard.chessBoardGrid.gridSize - 1 - Mathf.RoundToInt(localHitPoint.z / cellSideLength - 0.5f);

        int maxCellIndex = Mathf.Max(compChessBoard.chessBoardGrid.gridSize - 1, 0);
        nearestCellX = Mathf.Clamp(nearestCellX, 0, maxCellIndex);
        nearestCellZ = Mathf.Clamp(nearestCellZ, 0, maxCellIndex);

        RectCoordinates nearestCoords = new RectCoordinates(nearestCellX, nearestCellZ);
        int posIndex = compChessBoard.GetPosIndexByCoords(nearestCoords);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            SetAimChessPreviewActive(false);
            return;
        }

        EnsureAimChessPreview((PlayerFlag)curPlayer.playerFlag.value);
        if (aimChessPreview == null) {
            return;
        }

        Vector3 nearestCellCenterLocalPos = compChessBoard.chessBoardGrid.GetCellCenterLocalPosition(nearestCellX, nearestCellZ);
        aimChessPreview.transform.position = gridTransform.TransformPoint(nearestCellCenterLocalPos);
        aimCoords.SetValue(nearestCoords.x, nearestCoords.z);
        SetAimChessPreviewActive(true);
    }

    private void EnsureAimChessPreview(PlayerFlag playerFlag)
    {
        if (aimChessPreview != null && aimChessPreviewPlayerFlag == playerFlag) {
            return;
        }

        if (aimChessPreview != null) {
            GameObject.DestroyImmediate(aimChessPreview);
            aimChessPreview = null;
        }

        string gamePrefabTypeId = DuelUtils.GetGamePrefabTypeIdWithPlayerFlag(playerFlag);
        var gamePrefabCfg = GamePrefabDataType.GetConfigData(gamePrefabTypeId);
        if (gamePrefabCfg == null) {
            return;
        }

        aimChessPreview = Global.Instance.resourceManager.LoadGamePrefab(gamePrefabCfg.resPath);
        if (aimChessPreview == null) {
            return;
        }

        aimChessPreviewPlayerFlag = playerFlag;
        SetAimChessPreviewActive(false);
        foreach (var collider in aimChessPreview.GetComponentsInChildren<Collider>()) {
            collider.enabled = false;
        }
    }

    private void SetAimChessPreviewActive(bool isActive)
    {
        if (aimChessPreview != null) {
            aimChessPreview.SetActive(isActive);
        }
    }

    public void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        RefreshDuelHud();
    }

    public void OnDuelOwnershipResult(OnDuelOwnershipResult evt)
    {
        SetText(binder.txt_ownership_black_points, $"黑方目数: {FormatPointCount(evt.blackPoints)}");
        SetText(binder.txt_ownership_white_points, $"白方目数: {FormatPointCount(evt.whitePoints)}（贴目后）");
        SetOwnershipActive(true);
        SetOwnershipResultPanelVisible(true);
    }

    public void OnClearDuelOwnership(OnClearDuelOwnership evt)
    {
        SetOwnershipActive(false);
        SetOwnershipResultPanelVisible(false);
    }

    public void OnDuelScoreResult(OnDuelScoreResult evt)
    {
        if (evt.scoreResult == null) {
            return;
        }

        DuelScoreResult scoreResult = evt.scoreResult;
        if (evt.requireConfirm) {
            ConfirmPopup.UpdateOpenContent(
                pendingScorePopupRequestId,
                "确认数子结果",
                BuildScoreConfirmContent(scoreResult),
                () => EmitSystemEvent(new OnConfirmDuelScore(scoreResult)),
                true
            );
            pendingScorePopupRequestId = 0;
        }
    }

    public void OnDuelScoreFailed(OnDuelScoreFailed evt)
    {
        if (!evt.requireConfirm) {
            return;
        }

        ConfirmPopup.UpdateOpenContent(
            pendingScorePopupRequestId,
            "确认数子结果",
            "数子失败，请稍后重试。",
            null,
            false
        );
        pendingScorePopupRequestId = 0;
    }

    public void RefreshDuelHud()
    {
        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
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
            blackPlayer,
            curTurnPlayerGuid,
            "黑方"
        );
        RefreshPlayerInfoPanel(
            binder.txt_white_title,
            binder.txt_white_hold_time,
            binder.txt_white_byoyomi_count,
            binder.txt_white_byoyomi_time,
            whitePlayer,
            curTurnPlayerGuid,
            "白方"
        );

        RefreshGameEndResultPanel(mainScene, compDuel);
        RefreshSettingsActionVisibility(mainScene, compDuel);
    }

    public void OnMouse0Down()
    {
        if (IsSettingsPanelVisible()) {
            return;
        }

        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
        if (CanAcceptHumanTurnInput(mainScene, compDuel)) {
            EmitSystemEvent(new OnAddChessToBoard(aimCoords.Clone()));
        }
    }

    public void OnClickBtnSave()
    {
        EmitSystemEvent(new OnSaveDuelScene());
    }

    public void OnClickBtnExit()
    {
        ConfirmPopup.Show(
            "退出对局",
            "确认退出当前对局？",
            () => Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default)
        );
    }

    public void OnClickBtnOwnership()
    {
        if (isOwnershipVisible) {
            EmitSystemEvent(new OnRequestClearDuelOwnership());
            return;
        }

        SetOwnershipActive(true);
        SetOwnershipResultPanelVisible(false);
        EmitSystemEvent(new OnRequestDuelOwnership());
    }

    public void OnClickBtnPass()
    {
        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
        if (!CanAcceptHumanTurnInput(mainScene, compDuel)) {
            return;
        }

        EmitSystemEvent(new OnRequestDuelPass());
    }

    public void OnClickBtnRequestScore()
    {
        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.isScoring) {
            return;
        }

        CloseSettingsPanel();
        pendingScorePopupRequestId = ConfirmPopup.Show(
            "确认数子结果",
            "数子中...",
            null,
            null,
            "确认结果",
            "继续对局",
            false
        );
        EmitSystemEvent(new OnRequestDuelScore());
    }

    public void OnClickBtnResign()
    {
        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
        if (!CanResign(mainScene, compDuel)) {
            SetResignButtonVisible(false);
            return;
        }

        CloseSettingsPanel();

        Player curPlayer = mainScene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        string playerText = GetPlayerDisplayName(curPlayer, compDuel, compDuel?.curTurnPlayerGuid.value);

        ConfirmPopup.Show(
            "确认认输",
            $"确认{playerText}认输？",
            () => EmitSystemEvent(new OnConfirmDuelResign()),
            null,
            "确认认输",
            "继续对局"
        );
    }

    private void BindPrefabHud()
    {
        AddButtonListener(binder.btn_duel_settings, OpenSettingsPanel);
        AddButtonListener(binder.btn_duel_ownership, OnClickBtnOwnership);
        AddButtonListener(binder.btn_duel_pass, OnClickBtnPass);
        AddButtonListener(binder.btn_settings_request_score, OnClickBtnRequestScore);
        AddButtonListener(binder.btn_settings_resign, OnClickBtnResign);
        AddButtonListener(binder.btn_settings_save, OnClickBtnSave);
        AddButtonListener(binder.btn_settings_exit, OnClickBtnExit);
        AddButtonListener(binder.btn_settings_close, CloseSettingsPanel);
    }

    private void RefreshPlayerInfoPanel(
        TextMeshProUGUI titleText,
        TextMeshProUGUI holdText,
        TextMeshProUGUI byoyomiCountText,
        TextMeshProUGUI byoyomiTimeText,
        Player player,
        string curTurnPlayerGuid,
        string title
    )
    {
        bool isCurTurnPlayer = player != null && player.guid == curTurnPlayerGuid;
        SetText(titleText, isCurTurnPlayer ? $"{title}  行棋中" : title);

        var compDuelInfo = player?.GetComponent<ComponentDuelInfo>();
        if (compDuelInfo == null) {
            SetText(holdText, "持有时间: --");
            SetText(byoyomiCountText, "读秒次数: --");
            SetText(byoyomiTimeText, "读秒时间: --");
            return;
        }

        SetText(holdText, $"持有时间: {FormatSeconds(compDuelInfo.holdLeftSeconds.value, compDuelInfo.isInfiniteTime.value)}");
        SetText(byoyomiCountText, $"读秒次数: {compDuelInfo.byoyomiLeftCount.value}");
        SetText(byoyomiTimeText, $"读秒时间: {FormatSeconds(compDuelInfo.byoyomiLeftSeconds.value, false)}");
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

    private string BuildScoreConfirmContent(DuelScoreResult scoreResult)
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

    private void RefreshSettingsActionVisibility(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        SetResignButtonVisible(CanResign(mainScene, compDuel));
    }

    private bool CanResign(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        if (mainScene == null || compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return false;
        }

        if (compDuel.isScoring) {
            return false;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return false;
        }

        if (string.IsNullOrEmpty(compDuel.curTurnPlayerGuid.value)) {
            return false;
        }

        return CanAcceptHumanTurnInput(mainScene, compDuel)
            && mainScene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value) != null;
    }

    private bool CanAcceptHumanTurnInput(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        if (mainScene == null || compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return false;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return false;
        }

        return !compDuel.isAiDuel.value
            || string.IsNullOrEmpty(compDuel.aiPlayerGuid.value)
            || compDuel.curTurnPlayerGuid.value != compDuel.aiPlayerGuid.value;
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

    private string GetPlayerDisplayName(Player player, SceneComponentDuel compDuel, string playerGuid)
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

    private void OpenSettingsPanel()
    {
        SetSettingsPanelVisible(true);
    }

    private void CloseSettingsPanel()
    {
        SetSettingsPanelVisible(false);
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

    private void SetResignButtonVisible(bool isVisible)
    {
        if (binder.btn_settings_resign != null) {
            binder.btn_settings_resign.gameObject.SetActive(isVisible);
        }
    }

    private void SetOwnershipActive(bool isActive)
    {
        isOwnershipVisible = isActive;
        SetOwnershipButtonText(isActive);
    }

    private void SetOwnershipButtonText(bool isVisible)
    {
        SetText(binder.txt_duel_ownership_button, isVisible ? "关闭" : "形势");
    }

    private bool IsSettingsPanelVisible()
    {
        return binder.panel_duel_settings != null && binder.panel_duel_settings.activeSelf;
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }

    private void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value;
        }
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
