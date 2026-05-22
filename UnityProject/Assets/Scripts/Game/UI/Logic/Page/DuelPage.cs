using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XNClient.ChessBoard;

public class DuelPage : UIPageWithBinder<DuelPageUI>
{
    public override string pageName => UIPage.GetPageName<DuelPage>();

    private DuelPageBoardInputController boardInput;
    private DuelPageHudView hudView;
    private int pendingScorePopupRequestId;
    private int pendingTakeBackPopupRequestId;

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
        RegisterSystemEvent<OnDuelSaveResult>(OnDuelSaveResult);
        RegisterSystemEvent<OnApplyLanDuelScoreRequest>(OnApplyLanDuelScoreRequest);
        RegisterSystemEvent<OnLanDuelScoreConfirmRequest>(OnLanDuelScoreConfirmRequest);
        RegisterSystemEvent<OnLanDuelScoreResultConfirmRequest>(OnLanDuelScoreResultConfirmRequest);
        RegisterSystemEvent<OnLanDuelTakeBackConfirmRequest>(OnLanDuelTakeBackConfirmRequest);

        BindPrefabHud();
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        hudView.Reset();
        RefreshDuelHud();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        RefreshDuelHud();
        hudView.RefreshActionNotice();

        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(mainScene, compDuel);
        boardInput.Refresh(mainScene, compDuel, inputState, hudView.IsSettingsPanelVisible());

        if (Input.GetKeyDown(KeyCode.Mouse0) && !IsPointerOverUI()) {
            OnMouse0Down();
        }
    }

    protected override void OnClose()
    {
        ClosePendingScorePopup();
        ClosePendingTakeBackPopup();
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

    public void OnDuelSaveResult(OnDuelSaveResult evt)
    {
        hudView.ShowActionNotice(evt != null && evt.success
            ? MessageText.Get("duel_save_success")
            : MessageText.Get("duel_save_failed"));
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
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(mainScene, compDuel);
        if (boardInput.TryGetMoveCoords(inputState, out RectCoordinates coords)) {
            EmitSystemEvent(new OnSubmitDuelMove(coords));
        }
    }

    public void OnClickBtnSave()
    {
        EmitSystemEvent(new OnSaveDuelScene());
    }

    public void OnClickBtnExit()
    {
        ConfirmPopup.Show(
            MessageText.Get("duel_exit_title"),
            MessageText.Get("duel_exit_content"),
            () => Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default)
        );
    }

    public void OnClickBtnOwnership()
    {
        if (hudView.IsOwnershipVisible) {
            EmitSystemEvent(new OnRequestClearDuelOwnership());
            return;
        }

        hudView.BeginOwnershipRequest();
        EmitSystemEvent(new OnRequestDuelOwnership());
    }

    public void OnClickBtnPass()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !DuelInputAuthority.GetLocalState(mainScene, compDuel).CanSubmitMove) {
            return;
        }

        EmitSystemEvent(new OnSubmitDuelPass());
    }

    public void OnClickBtnRequestScore()
    {
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
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
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return;
        }

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
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(mainScene, compDuel);
        if (compDuel == null || !inputState.CanSubmitMove || !DuelPageInteractionState.CanResign(mainScene, compDuel)) {
            hudView.SetResignButtonVisible(false);
            return;
        }

        hudView.CloseSettingsPanel();

        Player curPlayer = mainScene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        string playerText = hudView.GetPlayerDisplayName(curPlayer, compDuel, compDuel.curTurnPlayerGuid.value);

        ConfirmPopup.Show(
            MessageText.Get("duel_resign_title"),
            MessageText.Format("duel_resign_content", playerText),
            () => EmitSystemEvent(new OnSubmitDuelResign()),
            null,
            MessageText.Get("duel_resign_confirm"),
            MessageText.Get("duel_continue_game")
        );
    }

    private void BindPrefabHud()
    {
        AddButtonListener(binder.btn_duel_settings, hudView.OpenSettingsPanel);
        AddButtonListener(binder.btn_duel_ownership, OnClickBtnOwnership);
        AddButtonListener(binder.btn_duel_pass, OnClickBtnPass);
        AddButtonListener(binder.btn_settings_request_score, OnClickBtnRequestScore);
        AddButtonListener(binder.btn_settings_take_back, OnClickBtnTakeBack);
        AddButtonListener(binder.btn_settings_resign, OnClickBtnResign);
        AddButtonListener(binder.btn_settings_save, OnClickBtnSave);
        AddButtonListener(binder.btn_settings_exit, OnClickBtnExit);
        AddButtonListener(binder.btn_settings_close, hudView.CloseSettingsPanel);
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
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

        EmitSystemEvent(new OnSubmitDuelTakeBack());
    }

    private void ClosePendingTakeBackPopup()
    {
        if (pendingTakeBackPopupRequestId <= 0) {
            return;
        }

        ConfirmPopup.CloseIfOpen(pendingTakeBackPopupRequestId);
        pendingTakeBackPopupRequestId = 0;
    }

    private void ClosePendingScorePopup()
    {
        if (pendingScorePopupRequestId <= 0) {
            return;
        }

        ConfirmPopup.CloseIfOpen(pendingScorePopupRequestId);
        pendingScorePopupRequestId = 0;
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
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
