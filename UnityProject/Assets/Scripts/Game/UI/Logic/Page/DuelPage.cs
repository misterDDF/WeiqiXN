using UnityEngine;
using UnityEngine.UI;
using XNClient.ChessBoard;

public class DuelPage : UIPageWithBinder<DuelPageUI>
{
    public override string pageName => UIPage.GetPageName<DuelPage>();

    private DuelPageBoardInputController boardInput;
    private DuelPageHudView hudView;
    private int pendingScorePopupRequestId;
    private int pendingTakeBackPopupRequestId;
    private int reconnectWaitingPopupRequestId;
    private int ogsReconnectWaitingPopupRequestId;
    private int pendingTakeBackMoveCount;
    private int pendingTakeBackRemoveCount;
    private string pendingTakeBackTurnPlayerGuid;
    private bool isMoveConfirmPopupOpen;

    protected override void OnLoaded()
    {
        base.OnLoaded();

        boardInput = new DuelPageBoardInputController();
        hudView = new DuelPageHudView(binder);

        RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);
        RegisterSystemEvent<OnDuelOwnershipResult>(OnDuelOwnershipResult);
        RegisterSystemEvent<OnClearDuelOwnership>(OnClearDuelOwnership);
        RegisterSystemEvent<OnDuelScoreResult>(OnDuelScoreResult);
        RegisterSystemEvent<OnDuelScoreFailed>(OnDuelScoreFailed);
        RegisterSystemEvent<OnDuelPassAccepted>(OnDuelPassAccepted);
        RegisterSystemEvent<OnDuelTakeBackResult>(OnDuelTakeBackResult);
        RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        RegisterSystemEvent<OnApplyLanDuelScoreRequest>(OnApplyLanDuelScoreRequest);
        RegisterSystemEvent<OnLanDuelScoreConfirmRequest>(OnLanDuelScoreConfirmRequest);
        RegisterSystemEvent<OnLanDuelScoreResultConfirmRequest>(OnLanDuelScoreResultConfirmRequest);
        RegisterSystemEvent<OnLanDuelTakeBackConfirmRequest>(OnLanDuelTakeBackConfirmRequest);
        RegisterSystemEvent<OnOgsDuelTakeBackConfirmRequest>(OnOgsDuelTakeBackConfirmRequest);
        RegisterSystemEvent<OnOgsStoneRemovalStateChanged>(OnOgsStoneRemovalStateChanged);
        RegisterSystemEvent<OnLanRoomPeerLeft>(OnLanRoomPeerLeft);
        RegisterSystemEvent<OnLanRoomReconnectWaiting>(OnLanRoomReconnectWaiting);
        RegisterSystemEvent<OnLanRoomReconnected>(OnLanRoomReconnected);
        RegisterSystemEvent<OnOgsDuelReconnectWaiting>(OnOgsDuelReconnectWaiting);
        RegisterSystemEvent<OnOgsDuelReconnected>(OnOgsDuelReconnected);

        BindPrefabHud();
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        hudView.Reset();
        RefreshDuelHud();
        if (Global.Instance.lanRoomService != null && Global.Instance.lanRoomService.IsReconnectWaiting) {
            ShowOrUpdateReconnectWaitingPopup(Global.Instance.lanRoomService.ReconnectWaitingSeconds);
        }
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        RefreshDuelHud();
        hudView.RefreshActionNotice();

        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        DuelInputAuthorityState inputState = GetCurrentInputState(mainScene, compDuel);
        boardInput.Refresh(mainScene, compDuel, inputState, hudView.IsSettingsPanelVisible());
        RefreshPortraitMoveConfirmation(mainScene, compDuel, inputState);

        if (Input.GetKeyDown(KeyCode.Mouse0) && !IsPointerOverUI()) {
            OnMouse0Down();
        }
    }

    protected override void OnClose()
    {
        ClosePendingScorePopup();
        ClosePendingTakeBackPopup();
        CloseReconnectWaitingPopup();
        CloseOgsReconnectWaitingPopup();
        CloseMoveConfirmPopup();
        boardInput.Dispose();
        base.OnClose();
    }

    public void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        RefreshDuelHud();
    }

    public void OnDuelOwnershipResult(OnDuelOwnershipResult evt)
    {
        hudView.OnDuelOwnershipResult(evt);
    }

    public void OnClearDuelOwnership(OnClearDuelOwnership evt)
    {
        hudView.ClearOwnership();
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
                MessageText.Get("duel_score_confirm_title"),
                hudView.BuildScoreConfirmContent(scoreResult),
                () => EmitSystemEvent(new OnConfirmDuelScore(scoreResult)),
                true
            );
            pendingScorePopupRequestId = 0;
        }
    }

    public void OnDuelScoreFailed(OnDuelScoreFailed evt)
    {
        if (!evt.requireConfirm) {
            ClosePendingScorePopup();
            string message = string.IsNullOrEmpty(evt.message) ? MessageText.Get("duel_score_failed_back_to_game") : evt.message;
            SceneComponentDuel compDuel = Global.Instance.sceneManager.mainScene?.GetComponent<SceneComponentDuel>();
            if (compDuel != null && compDuel.isLanDuel.value) {
                ConfirmPopup.ShowTip(MessageText.Get("duel_score_not_accepted_title"), message, null, MessageText.Get("common_confirm"));
            } else {
                hudView.ShowActionNotice(message);
            }
            return;
        }

        ConfirmPopup.UpdateOpenContent(
            pendingScorePopupRequestId,
            MessageText.Get("duel_score_confirm_title"),
            MessageText.Get("duel_score_retry_later"),
            null,
            false
        );
        pendingScorePopupRequestId = 0;
    }

    public void OnDuelPassAccepted(OnDuelPassAccepted evt)
    {
        hudView.OnDuelPassAccepted(evt);
    }

    public void OnDuelTakeBackResult(OnDuelTakeBackResult evt)
    {
        ClosePendingTakeBackPopup();
        if (evt == null || string.IsNullOrEmpty(evt.message)) {
            return;
        }

        hudView.ShowActionNotice(evt.message);
    }

    public void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        hudView.OnAfterAddChessToBoard(evt);
    }

    public void OnApplyLanDuelScoreRequest(OnApplyLanDuelScoreRequest evt)
    {
        if (evt == null || pendingScorePopupRequestId <= 0) {
            return;
        }

        ConfirmPopup.UpdateOpenContent(
            pendingScorePopupRequestId,
            MessageText.Get("duel_scoring_title"),
            MessageText.Get("duel_score_opponent_accepted"),
            null,
            false
        );
    }

    public void OnLanDuelScoreConfirmRequest(OnLanDuelScoreConfirmRequest evt)
    {
        if (evt == null) {
            return;
        }

        ConfirmPopup.Show(
            MessageText.Get("duel_score_request_title"),
            MessageText.Get("duel_score_request_content"),
            () => EmitSystemEvent(new OnSubmitLanDuelScoreConfirm(evt.request, true)),
            () => EmitSystemEvent(new OnSubmitLanDuelScoreConfirm(evt.request, false)),
            MessageText.Get("duel_score_accept_request"),
            MessageText.Get("duel_continue_game")
        );
    }

    public void OnLanDuelScoreResultConfirmRequest(OnLanDuelScoreResultConfirmRequest evt)
    {
        if (evt == null) {
            return;
        }

        ClosePendingScorePopup();
        pendingScorePopupRequestId = ConfirmPopup.Show(
            MessageText.Get("duel_score_confirm_title"),
            hudView.BuildScoreConfirmContent(BuildScoreResult(evt.result)),
            () => EmitSystemEvent(new OnSubmitLanDuelScoreResultConfirm(evt.result, true)),
            () => EmitSystemEvent(new OnSubmitLanDuelScoreResultConfirm(evt.result, false)),
            MessageText.Get("duel_score_accept_result"),
            MessageText.Get("duel_score_reject_result")
        );
    }

    public void OnLanDuelTakeBackConfirmRequest(OnLanDuelTakeBackConfirmRequest evt)
    {
        if (evt == null) {
            return;
        }

        ConfirmPopup.Show(
            MessageText.Get("duel_take_back_title"),
            MessageText.Get("duel_take_back_request_content"),
            () => EmitSystemEvent(new OnSubmitLanDuelTakeBackConfirm(evt.request, true)),
            () => EmitSystemEvent(new OnSubmitLanDuelTakeBackConfirm(evt.request, false)),
            MessageText.Get("duel_take_back_accept"),
            MessageText.Get("common_reject")
        );
    }

    public void OnOgsDuelTakeBackConfirmRequest(OnOgsDuelTakeBackConfirmRequest evt)
    {
        if (evt == null) {
            return;
        }

        ConfirmPopup.Show(
            MessageText.Get("duel_take_back_title"),
            MessageText.Get("duel_take_back_request_content"),
            () => EmitSystemEvent(new OnSubmitOgsDuelTakeBackConfirm(true)),
            () => EmitSystemEvent(new OnSubmitOgsDuelTakeBackConfirm(false)),
            MessageText.Get("duel_take_back_accept"),
            MessageText.Get("common_reject")
        );
    }

    public void OnOgsStoneRemovalStateChanged(OnOgsStoneRemovalStateChanged evt)
    {
        RefreshDuelHud();
    }

    public void OnLanRoomPeerLeft(OnLanRoomPeerLeft evt)
    {
        ClosePendingScorePopup();
        ClosePendingTakeBackPopup();
        CloseReconnectWaitingPopup();
        ConfirmPopup.ShowTip(
            MessageText.Get("lan_room_peer_left_title"),
            MessageText.Get("lan_room_peer_left_content"),
            () => Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default),
            MessageText.Get("common_confirm"));
    }

    public void OnLanRoomReconnectWaiting(OnLanRoomReconnectWaiting evt)
    {
        ClosePendingScorePopup();
        ClosePendingTakeBackPopup();
        ShowOrUpdateReconnectWaitingPopup(evt.elapsedSeconds);
    }

    public void OnLanRoomReconnected(OnLanRoomReconnected evt)
    {
        CloseReconnectWaitingPopup();
        hudView.ShowActionNotice(MessageText.Get("lan_room_reconnect_restored"));
    }

    public void OnOgsDuelReconnectWaiting(OnOgsDuelReconnectWaiting evt)
    {
        ClosePendingScorePopup();
        ClosePendingTakeBackPopup();
        CloseMoveConfirmPopup();
        ShowOrUpdateOgsReconnectWaitingPopup();
    }

    public void OnOgsDuelReconnected(OnOgsDuelReconnected evt)
    {
        CloseOgsReconnectWaitingPopup();
        hudView.ShowActionNotice("OGS 对局连接已恢复");
    }

    public void RefreshDuelHud()
    {
        hudView.Refresh(Global.Instance.sceneManager.mainScene);
    }

    public void OnMouse0Down()
    {
        if (hudView.IsSettingsPanelVisible()) {
            return;
        }

        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (mainScene is OgsDuelScene ogsScene) {
            OgsDuelSystem ogsDuelSystem = ogsScene.GetSystem<OgsDuelSystem>();
            if (ogsDuelSystem != null && ogsDuelSystem.IsInStoneRemovalPhase()) {
                SceneComponentChessBoard compChessBoard = mainScene.GetComponent<SceneComponentChessBoard>();
                if (TryGetBoardClickCoords(mainScene, compChessBoard, out RectCoordinates stoneRemovalCoords)) {
                    EmitSystemEvent(new OnSubmitOgsRemovedStoneToggle(stoneRemovalCoords));
                }
                return;
            }
        }

        DuelInputAuthorityState inputState = GetCurrentInputState(mainScene, compDuel);
        if (boardInput.TryGetMoveCoords(inputState, out RectCoordinates coords)) {
            if (ShouldUsePortraitMoveConfirm(mainScene, compDuel, inputState)) {
                BeginOrUpdateMoveConfirmation(mainScene, compDuel, inputState);
                return;
            }

            if (mainScene is OgsDuelScene) {
                EmitSystemEvent(new OnSubmitOgsDuelMove(coords));
                return;
            }

            EmitSystemEvent(new OnSubmitDuelMove(coords, inputState.localInputPlayerFlag));
        }
    }

    public void OnClickBtnExit()
    {
        ConfirmPopup.Show(
            MessageText.Get("duel_exit_title"),
            MessageText.Get("duel_exit_content"),
            ExitDuelToMainMenu
        );
    }

    private void ExitDuelToMainMenu()
    {
        SceneComponentDuel compDuel = Global.Instance.sceneManager.mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.isLanDuel.value) {
            Global.Instance.lanRoomService?.LeaveCurrentSession(LanRoomLeaveReason.ExitDuel);
        }

        Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
    }

    private void ExitReconnectWaitingToMainMenu()
    {
        Global.Instance.lanRoomService?.LeaveCurrentSession(LanRoomLeaveReason.ExitDuel, false);
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
    }

    private void ExitOgsReconnectWaitingToMainMenu()
    {
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
    }

    public void OnClickBtnOwnership()
    {
        OgsDuelSystem ogsDuelSystem = GetOgsDuelSystem();
        if (ogsDuelSystem != null && ogsDuelSystem.IsInStoneRemovalPhase()) {
            EmitSystemEvent(new OnSubmitOgsRemovedStonesAccept());
            return;
        }

        if (hudView.IsOwnershipVisible) {
            EmitSystemEvent(new OnRequestClearDuelOwnership());
            return;
        }

        DuelAiRecommendationSystem aiRecommendationSystem = Global.Instance.sceneManager.mainScene?.GetSystem<DuelAiRecommendationSystem>();
        if (aiRecommendationSystem != null && (aiRecommendationSystem.IsAiAnalyzing || aiRecommendationSystem.HasAiAnalysisRender)) {
            aiRecommendationSystem.ClearAiAnalysisRender();
        }

        hudView.BeginOwnershipRequest();
        EmitSystemEvent(new OnRequestDuelOwnership());
    }

    public void OnClickBtnPass()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        OgsDuelSystem ogsDuelSystem = GetOgsDuelSystem(mainScene);
        if (ogsDuelSystem != null && ogsDuelSystem.IsInStoneRemovalPhase()) {
            ConfirmPopup.Show(
                "拒绝数子",
                "您确定拒绝数子并返回对局吗？",
                () => EmitSystemEvent(new OnSubmitOgsRemovedStonesReject()),
                null,
                "确认",
                "取消"
            );
            return;
        }

        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        DuelInputAuthorityState inputState = GetCurrentInputState(mainScene, compDuel);
        if (compDuel == null || !inputState.CanSubmitMove) {
            return;
        }

        if (mainScene is OgsDuelScene) {
            EmitSystemEvent(new OnSubmitOgsDuelPass());
            return;
        }

        EmitSystemEvent(new OnSubmitDuelPass());
    }

    public void OnClickBtnRequestScore()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        if (mainScene is OgsDuelScene) {
            hudView.CloseSettingsPanel();
            hudView.ShowActionNotice("OGS 对局数子由连续虚手后的服务器确认流程处理。");
            return;
        }

        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel == null || compDuel.isScoring || !DuelInputAuthority.GetLocalState(mainScene, compDuel).CanSubmitMove) {
            return;
        }

        hudView.CloseSettingsPanel();
        if (compDuel.isLanDuel.value) {
            pendingScorePopupRequestId = ConfirmPopup.ShowBlocking(
                MessageText.Get("duel_score_wait_title"),
                MessageText.Get("duel_score_wait_content")
            );
            EmitSystemEvent(new OnSubmitDuelScore());
            return;
        }

        pendingScorePopupRequestId = ConfirmPopup.Show(
            MessageText.Get("duel_score_confirm_title"),
            MessageText.Get("duel_scoring_content"),
            null,
            null,
            MessageText.Get("duel_score_confirm_result"),
            MessageText.Get("duel_continue_game"),
            false
        );
        EmitSystemEvent(new OnSubmitDuelScore());
    }

    public void OnClickBtnTakeBack()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        if (mainScene is OgsDuelScene) {
            OgsDuelSystem ogsDuelSystem = mainScene.GetSystem<OgsDuelSystem>();
            if (ogsDuelSystem == null || !ogsDuelSystem.CanSubmitTakeBack()) {
                hudView.CloseSettingsPanel();
                hudView.ShowActionNotice(MessageText.Get("duel_take_back_unavailable"));
                return;
            }

            hudView.CloseSettingsPanel();
            ConfirmPopup.Show(
                MessageText.Get("duel_take_back_title"),
                MessageText.Get("duel_take_back_local_confirm_content"),
                () => EmitSystemEvent(new OnSubmitOgsDuelTakeBack()),
                null,
                MessageText.Get("duel_take_back_confirm"),
                MessageText.Get("common_cancel")
            );
            return;
        }

        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return;
        }

        int removeCount = DuelSystem.GetTakeBackMoveCountForState(compDuel);
        int moveCount = DuelMoveHistory.Count(compDuel.kataGoMoves);
        if (removeCount <= 0 || moveCount < removeCount) {
            hudView.CloseSettingsPanel();
            hudView.ShowActionNotice(MessageText.Get("duel_take_back_no_moves"));
            return;
        }

        pendingTakeBackMoveCount = moveCount;
        pendingTakeBackRemoveCount = removeCount;
        pendingTakeBackTurnPlayerGuid = compDuel.curTurnPlayerGuid.value;
        hudView.CloseSettingsPanel();
        ConfirmPopup.Show(
            MessageText.Get("duel_take_back_title"),
            compDuel.isLanDuel.value
                ? MessageText.Get("duel_take_back_lan_confirm_content")
                : MessageText.Get("duel_take_back_local_confirm_content"),
            () => SubmitConfirmedTakeBack(),
            null,
            MessageText.Get("duel_take_back_confirm"),
            MessageText.Get("common_cancel")
        );
    }

    public void OnClickBtnResign()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        if (mainScene is OgsDuelScene) {
            OgsDuelSystem ogsDuelSystem = mainScene.GetSystem<OgsDuelSystem>();
            if (ogsDuelSystem == null || !ogsDuelSystem.CanSubmitResign()) {
                hudView.SetResignButtonVisible(false);
                return;
            }

            hudView.CloseSettingsPanel();
            SceneComponentDuel ogsCompDuel = mainScene.GetComponent<SceneComponentDuel>();
            Player ogsCurPlayer = ogsCompDuel != null ? mainScene.GetEntity<Player>(ogsCompDuel.curTurnPlayerGuid.value) : null;
            string ogsPlayerText = hudView.GetPlayerDisplayName(ogsCurPlayer, ogsCompDuel, ogsCompDuel?.curTurnPlayerGuid.value);
            ConfirmPopup.Show(
                MessageText.Get("duel_resign_title"),
                MessageText.Format("duel_resign_content", ogsPlayerText),
                () => EmitSystemEvent(new OnSubmitOgsDuelResign()),
                null,
                MessageText.Get("duel_resign_confirm"),
                MessageText.Get("duel_continue_game")
            );
            return;
        }

        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(mainScene, compDuel);
        if (compDuel == null || !inputState.CanSubmitMove || !DuelPageInteractionState.CanResign(mainScene, compDuel)) {
            hudView.SetResignButtonVisible(false);
            return;
        }

        hudView.CloseSettingsPanel();

        Player curPlayer = mainScene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        string playerText = hudView.GetPlayerDisplayName(curPlayer, compDuel, compDuel.curTurnPlayerGuid.value);
        string loserGuid = compDuel.curTurnPlayerGuid.value;
        int moveCount = DuelMoveHistory.Count(compDuel.kataGoMoves);

        ConfirmPopup.Show(
            MessageText.Get("duel_resign_title"),
            MessageText.Format("duel_resign_content", playerText),
            () => EmitSystemEvent(new OnSubmitDuelResign(loserGuid, moveCount)),
            null,
            MessageText.Get("duel_resign_confirm"),
            MessageText.Get("duel_continue_game")
        );
    }

    private void BindPrefabHud()
    {
        AddButtonListener(binder.btn_duel_settings, hudView.OpenSettingsPanel);
        AddButtonListener(binder.btn_duel_ownership, OnClickBtnOwnership);
        AddButtonListener(binder.btn_duel_ai_analysis, OnClickBtnAiAnalysis);
        AddButtonListener(binder.btn_duel_pass, OnClickBtnPass);
        AddButtonListener(binder.btn_settings_request_score, OnClickBtnRequestScore);
        AddButtonListener(binder.btn_settings_take_back, OnClickBtnTakeBack);
        AddButtonListener(binder.btn_settings_resign, OnClickBtnResign);
        AddButtonListener(binder.btn_settings_exit, OnClickBtnExit);
        AddButtonListener(binder.btn_settings_close, hudView.CloseSettingsPanel);
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }

    private void OnClickBtnAiAnalysis()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        if (mainScene is OgsDuelScene) {
            hudView.ShowActionNotice("OGS 对局暂不支持本地 AI 分析。");
            return;
        }

        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel == null || compDuel.isLanDuel.value) {
            return;
        }

        DuelAiRecommendationSystem aiRecommendationSystem = mainScene.GetSystem<DuelAiRecommendationSystem>();
        if (aiRecommendationSystem == null) {
            return;
        }

        if (aiRecommendationSystem.HasAiAnalysisRender) {
            aiRecommendationSystem.ClearAiAnalysisRender();
            return;
        }

        if (hudView.IsOwnershipVisible) {
            EmitSystemEvent(new OnRequestClearDuelOwnership());
        }

        aiRecommendationSystem.RequestAiAnalysis();
    }

    private void SubmitConfirmedTakeBack()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return;
        }

        if (compDuel.isLanDuel.value) {
            pendingTakeBackPopupRequestId = ConfirmPopup.ShowBlocking(
                MessageText.Get("duel_take_back_wait_title"),
                MessageText.Get("duel_take_back_wait_content")
            );
        }

        EmitSystemEvent(new OnSubmitDuelTakeBack(
            pendingTakeBackRemoveCount,
            pendingTakeBackMoveCount,
            pendingTakeBackTurnPlayerGuid));
        ClearPendingTakeBackRequest();
    }

    private void ClosePendingTakeBackPopup()
    {
        if (pendingTakeBackPopupRequestId <= 0) {
            ClearPendingTakeBackRequest();
            return;
        }

        ConfirmPopup.CloseIfOpen(pendingTakeBackPopupRequestId);
        pendingTakeBackPopupRequestId = 0;
        ClearPendingTakeBackRequest();
    }

    private void ClearPendingTakeBackRequest()
    {
        pendingTakeBackMoveCount = 0;
        pendingTakeBackRemoveCount = 0;
        pendingTakeBackTurnPlayerGuid = null;
    }

    private void ClosePendingScorePopup()
    {
        if (pendingScorePopupRequestId <= 0) {
            return;
        }

        ConfirmPopup.CloseIfOpen(pendingScorePopupRequestId);
        pendingScorePopupRequestId = 0;
    }

    private void ShowOrUpdateReconnectWaitingPopup(int elapsedSeconds)
    {
        string title = MessageText.Get("lan_room_reconnect_wait_title");
        string content = MessageText.Format("lan_room_reconnect_wait_content", elapsedSeconds);
        if (reconnectWaitingPopupRequestId <= 0) {
            reconnectWaitingPopupRequestId = ConfirmPopup.ShowTip(
                title,
                content,
                ExitReconnectWaitingToMainMenu,
                MessageText.Get("lan_room_reconnect_leave_main_menu"));
            return;
        }

        ConfirmPopup.UpdateOpenContent(
            reconnectWaitingPopupRequestId,
            title,
            content,
            ExitReconnectWaitingToMainMenu,
            true);
    }

    private void CloseReconnectWaitingPopup()
    {
        if (reconnectWaitingPopupRequestId <= 0) {
            return;
        }

        ConfirmPopup.CloseIfOpen(reconnectWaitingPopupRequestId);
        reconnectWaitingPopupRequestId = 0;
    }

    private void ShowOrUpdateOgsReconnectWaitingPopup()
    {
        const string title = "OGS 重连中";
        const string content = "正在恢复 OGS 对局连接，恢复后将自动继续对局。";
        const string buttonText = "返回主菜单";

        if (ogsReconnectWaitingPopupRequestId <= 0) {
            ogsReconnectWaitingPopupRequestId = ConfirmPopup.ShowTip(
                title,
                content,
                ExitOgsReconnectWaitingToMainMenu,
                buttonText);
            return;
        }

        ConfirmPopup.UpdateOpenContent(
            ogsReconnectWaitingPopupRequestId,
            title,
            content,
            ExitOgsReconnectWaitingToMainMenu,
            true);
    }

    private void CloseOgsReconnectWaitingPopup()
    {
        if (ogsReconnectWaitingPopupRequestId <= 0) {
            return;
        }

        ConfirmPopup.CloseIfOpen(ogsReconnectWaitingPopupRequestId);
        ogsReconnectWaitingPopupRequestId = 0;
    }

    private void RefreshPortraitMoveConfirmation(SceneBase mainScene, SceneComponentDuel compDuel, DuelInputAuthorityState inputState)
    {
        if (!isMoveConfirmPopupOpen) {
            return;
        }

        if (!ShouldUsePortraitMoveConfirm(mainScene, compDuel, inputState) || hudView.IsSettingsPanelVisible()) {
            CloseMoveConfirmPopup();
            return;
        }

        if (!boardInput.IsPendingMoveActive) {
            CloseMoveConfirmPopup();
            return;
        }

        if (Input.GetKey(KeyCode.Mouse0) && !IsPointerOverUI()) {
            boardInput.TryBeginOrUpdatePendingMove(mainScene, compDuel, inputState);
        }
    }

    private bool BeginOrUpdateMoveConfirmation(SceneBase mainScene, SceneComponentDuel compDuel, DuelInputAuthorityState inputState)
    {
        if (!boardInput.TryBeginOrUpdatePendingMove(mainScene, compDuel, inputState)) {
            return false;
        }

        isMoveConfirmPopupOpen = true;
        DuelMoveConfirmPopup.Show(
            ConfirmPortraitMove,
            CancelPortraitMove,
            AdjustPortraitMove);
        return true;
    }

    private void ConfirmPortraitMove()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        DuelInputAuthorityState inputState = GetCurrentInputState(mainScene, compDuel);
        if (!boardInput.TryGetPendingMoveCoords(inputState, out RectCoordinates coords)) {
            CancelPortraitMove();
            return;
        }

        isMoveConfirmPopupOpen = false;
        boardInput.ClearPendingMove();
        if (mainScene is OgsDuelScene) {
            EmitSystemEvent(new OnSubmitOgsDuelMove(coords));
            return;
        }

        EmitSystemEvent(new OnSubmitDuelMove(coords, inputState.localInputPlayerFlag));
    }

    private void CancelPortraitMove()
    {
        isMoveConfirmPopupOpen = false;
        boardInput.ClearPendingMove();
    }

    private void AdjustPortraitMove(int offsetX, int offsetZ)
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        DuelInputAuthorityState inputState = GetCurrentInputState(mainScene, compDuel);
        boardInput.TryMovePendingMove(mainScene, compDuel, inputState, offsetX, offsetZ);
    }

    private void CloseMoveConfirmPopup()
    {
        if (!isMoveConfirmPopupOpen) {
            return;
        }

        isMoveConfirmPopupOpen = false;
        boardInput.ClearPendingMove();
        Global.Instance.uiManager.TryClosePage<DuelMoveConfirmPopup>();
    }

    private bool ShouldUsePortraitMoveConfirm(SceneBase mainScene, SceneComponentDuel compDuel, DuelInputAuthorityState inputState)
    {
        return mainScene != null
            && compDuel != null
            && inputState.CanSubmitMove
            && UIUtils.IsPortrait(rectTransform);
    }

    private DuelScoreResult BuildScoreResult(LanDuelScoreResultMessage result)
    {
        return new DuelScoreResult
        {
            blackScore = result.blackScore,
            whiteScore = result.whiteScore,
            komi = result.komi,
            margin = result.margin,
            winnerFlag = result.winnerFlag,
            scoreSource = result.scoreSource,
        };
    }

    private bool IsPointerOverUI()
    {
        return UIUtils.IsPointerOverUI();
    }

    private bool TryGetBoardClickCoords(SceneBase mainScene, SceneComponentChessBoard compChessBoard, out RectCoordinates coords)
    {
        coords = null;
        if (mainScene == null || compChessBoard?.chessBoardGrid == null) {
            return false;
        }

        Camera sceneCamera = Global.Instance.uiManager.GetSceneCamera();
        if (sceneCamera == null) {
            return false;
        }

        Ray mouseRay = sceneCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(mouseRay.origin, mouseRay.direction, out RaycastHit hitInfo, 500)) {
            return false;
        }

        Transform gridTransform = compChessBoard.chessBoardGrid.transform;
        Vector3 localHitPoint = gridTransform.InverseTransformPoint(hitInfo.point);
        float cellSideLength = ChessBoardConfig.rectCellSideLength;
        float boardSideLength = compChessBoard.chessBoardGrid.gridSize * cellSideLength;
        if (localHitPoint.x < 0f || localHitPoint.x > boardSideLength || localHitPoint.z < 0f || localHitPoint.z > boardSideLength) {
            return false;
        }

        int nearestCellX = Mathf.RoundToInt(localHitPoint.x / cellSideLength - 0.5f);
        int nearestCellZ = compChessBoard.chessBoardGrid.gridSize - 1 - Mathf.RoundToInt(localHitPoint.z / cellSideLength - 0.5f);
        int maxCellIndex = Mathf.Max(compChessBoard.chessBoardGrid.gridSize - 1, 0);
        nearestCellX = Mathf.Clamp(nearestCellX, 0, maxCellIndex);
        nearestCellZ = Mathf.Clamp(nearestCellZ, 0, maxCellIndex);
        coords = new RectCoordinates(nearestCellX, nearestCellZ);
        return true;
    }

    private DuelInputAuthorityState GetCurrentInputState(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        if (mainScene is OgsDuelScene) {
            OgsDuelSystem ogsDuelSystem = GetOgsDuelSystem(mainScene);
            return ogsDuelSystem != null ? ogsDuelSystem.GetInputState() : default;
        }

        return DuelInputAuthority.GetLocalState(mainScene, compDuel);
    }

    private OgsDuelSystem GetOgsDuelSystem()
    {
        return GetOgsDuelSystem(Global.Instance.sceneManager.mainScene);
    }

    private OgsDuelSystem GetOgsDuelSystem(SceneBase mainScene)
    {
        return mainScene is OgsDuelScene
            ? mainScene.GetSystem<OgsDuelSystem>()
            : null;
    }
}
