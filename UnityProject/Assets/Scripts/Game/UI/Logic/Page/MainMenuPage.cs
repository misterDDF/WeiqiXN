using System.Threading;
using UnityEngine;
using XNClient.Logger;

public class MainMenuPage : UIPageWithBinder<MainMenuPageUI>
{
    private const float FriendInvitationBadgeRefreshIntervalSeconds = 15f;

    public override string pageName => UIPage.GetPageName<MainMenuPage>();
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;
    private bool lastOgsButtonVisible;
    private bool isOgsGameStarting;
    private bool isFriendInvitationBadgeRefreshing;
    private float nextFriendInvitationBadgeRefreshTime;

    protected override void OnLoaded()
    {
        base.OnLoaded();

        ApplyCurrentLayoutState(true);

        binder.btn_new_game.onClick.AddListener(OnClickBtnNewGame);
        binder.btn_ai_game.onClick.AddListener(OnClickBtnAiGame);
        binder.btn_lan_game.onClick.AddListener(OnClickBtnLanGame);
        binder.btn_ogs_game.onClick.AddListener(OnClickBtnOgsGame);
        binder.btn_exit.onClick.AddListener(OnClickBtnExit);
        binder.btn_user_info.onClick.AddListener(OnClickBtnUserInfo);
        RegisterSystemEvent<OnOgsFriendInvitationCountChanged>(OnOgsFriendInvitationCountChanged);

        RefreshOgsGameButton(true);
        SetUserInfoRedDotVisible(false);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        ApplyCurrentLayoutState(false);
        RefreshOgsGameButton(false);
        RefreshFriendInvitationBadge(true);
        Global.Instance.ogsChallengeInviteCoordinator?.RequestImmediatePoll();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ApplyCurrentLayoutState(false);
        RefreshOgsGameButton(false);
        if (Time.unscaledTime >= nextFriendInvitationBadgeRefreshTime) {
            RefreshFriendInvitationBadge(false);
        }
    }

    private void ApplyCurrentLayoutState(bool force)
    {
        if (binder.sr_platform == null) {
            return;
        }

        bool isPortrait = UIUtils.IsPortrait(rectTransform);
        if (!force && hasAppliedLayoutState && isPortrait == lastPortraitLayout) {
            return;
        }

        binder.SetSrPlatformState(isPortrait ? MainMenuPageUI.SrPlatformState.Portrait : MainMenuPageUI.SrPlatformState.Landscape, force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;
    }

    public void OnClickBtnNewGame()
    {
        DuelSetupPopup.Open(false);
    }

    public void OnClickBtnAiGame()
    {
        DuelSetupPopup.Open(true);
    }

    public void OnClickBtnLanGame()
    {
        LanRoomPopup.OpenPopup();
    }

    public async void OnClickBtnOgsGame()
    {
        if (isOgsGameStarting) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasWriteSession) {
            RefreshOgsGameButton(true);
            string message = service != null && service.HasSession
                ? "当前 OGS 授权缺少对局权限，请重新登录 OGS。"
                : "请先登录 OGS。";
            ConfirmPopup.ShowTip("OGS 对战", message, null, "确定");
            return;
        }

        isOgsGameStarting = true;
        RefreshOgsGameButton(true);
        int popupRequestId = ConfirmPopup.ShowBlocking("OGS 对战", "正在检查进行中的 OGS 对局...");

        try {
            OgsBotGameStartResult activeGame = await service.LoadCurrentActiveGameAsync();
            if (activeGame != null && !activeGame.success) {
                XNLogger.LogWarn(
                    "OGS active game load failed.",
                    ("message", activeGame.message),
                    ("gameId", activeGame.gameId.ToString()),
                    ("opponentId", activeGame.botId.ToString()));
                ConfirmPopup.CloseIfOpen(popupRequestId);
                ConfirmPopup.ShowTip("OGS 对战失败", activeGame.message, null, "确定");
                return;
            }

            ConfirmPopup.CloseIfOpen(popupRequestId);
            if (activeGame != null) {
                OgsDuelLaunchFlow.EnterOgsDuelScene(activeGame);
                return;
            }

            DuelSetupPopup.OpenForOgs(StartOgsAutomatchWithConfig);
        }
        catch (System.Exception ex) {
            XNLogger.LogError("OGS game start from main menu failed.", ("err", ex.Message));
            ConfirmPopup.CloseIfOpen(popupRequestId);
            ConfirmPopup.ShowTip("OGS 对战失败", ex.Message, null, "确定");
        }
        finally {
            isOgsGameStarting = false;
            RefreshOgsGameButton(true);
        }
    }

    public void OnClickBtnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        UnityEngine.Application.Quit();
#endif
    }

    public void OnClickBtnUserInfo()
    {
        Global.Instance.uiManager.ShowPage<UserInfoPopup>();
    }

    private void RefreshOgsGameButton(bool force)
    {
        bool visible = Global.Instance.ogsConnectionService != null && Global.Instance.ogsConnectionService.HasWriteSession;
        if (force || visible != lastOgsButtonVisible || binder.btn_ogs_game.gameObject.activeSelf != visible) {
            binder.btn_ogs_game.gameObject.SetActive(visible);
            lastOgsButtonVisible = visible;
        }

        binder.btn_ogs_game.interactable = visible && !isOgsGameStarting;
    }

    private async void RefreshFriendInvitationBadge(bool force)
    {
        if (isFriendInvitationBadgeRefreshing) {
            return;
        }

        nextFriendInvitationBadgeRefreshTime = Time.unscaledTime + FriendInvitationBadgeRefreshIntervalSeconds;
        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasSession) {
            SetUserInfoRedDotVisible(false);
            service?.EmitFriendInvitationCountChanged(0);
            return;
        }

        isFriendInvitationBadgeRefreshing = true;
        try {
            OgsFriendInvitationCountResult result = await service.RequestFriendInvitationCountAsync();
            if (!result.success) {
                if (force) {
                    SetUserInfoRedDotVisible(false);
                }
                XNLogger.LogWarn("Refresh OGS friend invitation badge on main menu failed.", ("message", result.message));
                return;
            }

        }
        catch (System.Exception ex) {
            if (force) {
                SetUserInfoRedDotVisible(false);
            }
            XNLogger.LogWarn("Refresh OGS friend invitation badge on main menu exception.", ("err", ex.Message));
        }
        finally {
            isFriendInvitationBadgeRefreshing = false;
        }
    }

    private void SetUserInfoRedDotVisible(bool visible)
    {
        if (binder.red_dot_user_info != null) {
            binder.red_dot_user_info.gameObject.SetActive(visible);
        }
    }

    private void OnOgsFriendInvitationCountChanged(OnOgsFriendInvitationCountChanged evt)
    {
        SetUserInfoRedDotVisible(evt != null && evt.count > 0);
        nextFriendInvitationBadgeRefreshTime = Time.unscaledTime + FriendInvitationBadgeRefreshIntervalSeconds;
    }

    private async void StartOgsAutomatchWithConfig(DuelSceneCreateParamas duelParams)
    {
        if (isOgsGameStarting || duelParams == null) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasWriteSession) {
            RefreshOgsGameButton(true);
            ConfirmPopup.ShowTip("OGS 对战", "请先登录 OGS。", null, "确定");
            return;
        }

        isOgsGameStarting = true;
        RefreshOgsGameButton(true);
        OgsAutomatchCreateParams createParams = OgsDuelLaunchFlow.BuildAutomatchCreateParams(duelParams);
        bool canceledByUser = false;
        var cancelSource = new CancellationTokenSource();
        int popupRequestId = ConfirmPopup.ShowCancelableBlocking(
            "OGS 对战",
            "寻找对局中...",
            () => {
                canceledByUser = true;
                cancelSource.Cancel();
            },
            "取消");

        try {
            OgsBotGameStartResult result = await service.StartAutomatchGameAsync(createParams, cancellationToken: cancelSource.Token);
            if (!result.success) {
                if (canceledByUser) {
                    return;
                }

                XNLogger.LogWarn(
                    "OGS automatch game start failed.",
                    ("message", result.message),
                    ("gameId", result.gameId.ToString()));
                ConfirmPopup.CloseIfOpen(popupRequestId);
                ConfirmPopup.ShowTip("OGS 对战失败", result.message, null, "确定");
                return;
            }

            ConfirmPopup.CloseIfOpen(popupRequestId);
            OgsDuelLaunchFlow.EnterOgsDuelScene(result, duelParams);
        }
        catch (System.Exception ex) {
            XNLogger.LogError("OGS automatch game start from setup failed.", ("err", ex.Message));
            ConfirmPopup.CloseIfOpen(popupRequestId);
            ConfirmPopup.ShowTip("OGS 对战失败", ex.Message, null, "确定");
        }
        finally {
            cancelSource.Dispose();
            isOgsGameStarting = false;
            RefreshOgsGameButton(true);
        }
    }
}
