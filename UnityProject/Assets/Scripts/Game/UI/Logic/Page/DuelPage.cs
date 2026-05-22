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
                "确认数子结果",
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
            string message = string.IsNullOrEmpty(evt.message) ? "数子失败，已回到对局" : evt.message;
            SceneComponentDuel compDuel = Global.Instance.sceneManager.mainScene?.GetComponent<SceneComponentDuel>();
            if (compDuel != null && compDuel.isLanDuel.value) {
                ConfirmPopup.ShowTip("数子未通过", message, null, "确认");
            } else {
                hudView.ShowActionNotice(message);
            }
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
        hudView.ShowActionNotice(evt != null && evt.success ? "对局已保存" : "对局保存失败");
    }

    public void OnApplyLanDuelScoreRequest(OnApplyLanDuelScoreRequest evt)
    {
        if (evt == null || pendingScorePopupRequestId <= 0) {
            return;
        }

        ConfirmPopup.UpdateOpenContent(
            pendingScorePopupRequestId,
            "数子中",
            "对方已同意数子，正在计算结果。",
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
            "确认数子",
            "对方请求数子，是否同意结束对局并按当前局面计算结果？",
            () => EmitSystemEvent(new OnSubmitLanDuelScoreConfirm(evt.request, true)),
            () => EmitSystemEvent(new OnSubmitLanDuelScoreConfirm(evt.request, false)),
            "同意数子",
            "继续对局"
        );
    }

    public void OnLanDuelScoreResultConfirmRequest(OnLanDuelScoreResultConfirmRequest evt)
    {
        if (evt == null) {
            return;
        }

        ClosePendingScorePopup();
        pendingScorePopupRequestId = ConfirmPopup.Show(
            "确认数子结果",
            hudView.BuildScoreConfirmContent(BuildScoreResult(evt.result)),
            () => EmitSystemEvent(new OnSubmitLanDuelScoreResultConfirm(evt.result, true)),
            () => EmitSystemEvent(new OnSubmitLanDuelScoreResultConfirm(evt.result, false)),
            "接受结果",
            "不接受"
        );
    }

    public void OnLanDuelTakeBackConfirmRequest(OnLanDuelTakeBackConfirmRequest evt)
    {
        if (evt == null) {
            return;
        }

        ConfirmPopup.Show(
            "确认悔棋",
            "对方请求悔棋，是否同意回退上一手？",
            () => EmitSystemEvent(new OnSubmitLanDuelTakeBackConfirm(evt.request, true)),
            () => EmitSystemEvent(new OnSubmitLanDuelTakeBackConfirm(evt.request, false)),
            "同意悔棋",
            "拒绝"
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
            "退出对局",
            "确认退出当前对局？",
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
                "等待数子确认",
                "已发送数子请求，正在等待对方同意。"
            );
            EmitSystemEvent(new OnSubmitDuelScore());
            return;
        }

        pendingScorePopupRequestId = ConfirmPopup.Show(
            "确认数子结果",
            "数子中...",
            null,
            null,
            "确认结果",
            "继续对局",
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
            "确认悔棋",
            compDuel.isLanDuel.value ? "确认向对方请求悔棋？" : "确认悔棋？",
            () => SubmitConfirmedTakeBack(),
            null,
            "确认悔棋",
            "取消"
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
            "确认认输",
            $"确认{playerText}认输？",
            () => EmitSystemEvent(new OnSubmitDuelResign()),
            null,
            "确认认输",
            "继续对局"
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
                "等待悔棋确认",
                "已发送悔棋请求，正在等待对方同意。"
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
