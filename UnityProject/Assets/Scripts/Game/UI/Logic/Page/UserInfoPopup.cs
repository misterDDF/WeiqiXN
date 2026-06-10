using System.Collections.Generic;
using TMPro;
using UnityEngine;
using XNClient.Logger;

public class UserInfoPopup : UIPageWithBinder<UserInfoPopupUI>
{
    private static readonly Color OgsAvatarEmptyColor = new Color(0.78f, 0.62f, 0.28f, 1f);

    private readonly List<DuelReplayIndexItem> replayItems = new List<DuelReplayIndexItem>();
    private readonly List<ReplayArchiveItemWidget> replayItemWidgets = new List<ReplayArchiveItemWidget>();
    private int replayPageIndex;
    private bool replayLoadFailed;
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;
    private bool isOgsLoginRunning;
    private bool isOgsProfileRefreshRunning;
    private RemoteImageView ogsAvatarImage;

    public override string pageName => UIPage.GetPageName<UserInfoPopup>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        ApplyCurrentLayoutState(true);
        ogsAvatarImage = new RemoteImageView(binder, binder.img_ogs_avatar, OgsAvatarEmptyColor);

        binder.btn_close.onClick.AddListener(OnClickBtnClose);
        binder.btn_edit_name.onClick.AddListener(OnClickBtnEditName);
        binder.btn_login_ogs.onClick.AddListener(OnClickBtnLoginOgs);
        if (binder.btn_ogs_refresh != null) {
            binder.btn_ogs_refresh.onClick.AddListener(OnClickBtnRefreshOgs);
        }
        if (binder.btn_ogs_logout != null) {
            binder.btn_ogs_logout.onClick.AddListener(OnClickBtnLogoutOgs);
        }
        if (binder.btn_ogs_retry != null) {
            binder.btn_ogs_retry.onClick.AddListener(OnClickBtnRefreshOgs);
        }
        if (binder.btn_open_ogs_friends != null) {
            binder.btn_open_ogs_friends.onClick.AddListener(OnClickBtnOpenOgsFriends);
        }
        if (binder.btn_open_recent_replays != null) {
            binder.btn_open_recent_replays.onClick.AddListener(OnClickBtnOpenRecentReplays);
        }
        RegisterSystemEvent<OnOgsFriendInvitationCountChanged>(OnOgsFriendInvitationCountChanged);
        if (binder.btn_replay_prev != null) {
            binder.btn_replay_prev.onClick.AddListener(OnClickBtnReplayPrev);
        }
        if (binder.btn_replay_next != null) {
            binder.btn_replay_next.onClick.AddListener(OnClickBtnReplayNext);
        }
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        ApplyCurrentLayoutState(false);
        RefreshUserInfo();
        RefreshOgsAccountFromSession();
        RefreshReplayItems();
        ApplyCurrentFriendInvitationBadge();
        SetSaveTip(string.Empty);
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ApplyCurrentLayoutState(false);
    }

    protected override void OnClose()
    {
        ClearReplayItemWidgets();
        ogsAvatarImage?.Clear();
        base.OnClose();
    }

    public void OnClickBtnClose()
    {
        ClosePage();
    }

    private void OnClickBtnOpenOgsFriends()
    {
        Global.Instance.uiManager.ShowPage<OgsFriendListPopup>();
    }

    private void OnClickBtnOpenRecentReplays()
    {
        Global.Instance.uiManager.ShowPage<RecentReplayListPopup>();
    }

    public void OnClickBtnEditName()
    {
        ConfirmPopup.ShowInput(
            "修改姓名",
            "请输入新的本地用户名",
            User.Instance.compUserInfo.userName.value,
            SaveUserName,
            null,
            "保存",
            "取消"
        );
    }

    private void SaveUserName(string userName)
    {
        User.Instance.compUserInfo.Rename(userName);
        User.Instance.Save();
        RefreshUserInfo();
        SetSaveTip("已保存");
        Global.Instance.lanRoomService?.SyncLocalPlayerProfile();
    }

    private async void OnClickBtnLoginOgs()
    {
        XNLogger.LogInfo("OGS login button clicked.");
        if (isOgsLoginRunning || isOgsProfileRefreshRunning) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null) {
            SetSaveTip("OGS 登录服务不可用");
            SetOgsError("OGS 登录服务不可用");
            return;
        }

        isOgsLoginRunning = true;
        SetOgsButtonsInteractable(false);
        SetOgsState(UserInfoPopupUI.SrOgsAccountState.Loading);
        SetText(binder.txt_ogs_loading, "正在打开 OGS 登录...");
        SetSaveTip("正在打开 OGS 登录...");

        try {
            OgsConnectionResult result = await service.LoginWithBrowserCallbackAsync();
            if (!result.success) {
                SetSaveTip($"OGS 登录失败：{result.message}");
                SetOgsError(result.message);
                ConfirmPopup.ShowTip("OGS 登录失败", result.message, null, "确定");
                return;
            }

            OgsSession session = service.Session;
            string ogsName = session.DisplayName;
            ApplyOgsSession(session);
            Global.Instance.ogsChallengeInviteCoordinator?.RequestImmediatePoll();
            SetSaveTip(string.IsNullOrWhiteSpace(ogsName) ? "OGS 登录成功" : $"已登录 OGS：{ogsName}");
        }
        catch (System.Exception ex) {
            XNLogger.LogError("OGS login from user info popup failed.", ("err", ex.Message));
            SetSaveTip($"OGS 登录失败：{ex.Message}");
            SetOgsError(ex.Message);
            ConfirmPopup.ShowTip("OGS 登录失败", ex.Message, null, "确定");
        }
        finally {
            isOgsLoginRunning = false;
            SetOgsButtonsInteractable(true);
        }
    }

    private void RefreshUserInfo()
    {
        User.Instance.compUserInfo.EnsureValidUserInfo();
        binder.txt_user_id.text = $"ID: {User.Instance.compUserInfo.userId.value}";
        if (binder.txt_user_name != null) {
            binder.txt_user_name.text = User.Instance.compUserInfo.userName.value;
        }
        binder.txt_win_count.text = User.Instance.compUserInfo.winCount.value.ToString();
        binder.txt_lose_count.text = User.Instance.compUserInfo.loseCount.value.ToString();
    }

    private void SetSaveTip(string message)
    {
        if (binder.txt_save_tip != null) {
            binder.txt_save_tip.text = message ?? string.Empty;
        }
    }

    private async void OnClickBtnRefreshOgs()
    {
        if (isOgsLoginRunning || isOgsProfileRefreshRunning) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null) {
            SetOgsError("OGS 登录服务不可用");
            SetSaveTip("OGS 登录服务不可用");
            return;
        }

        OgsSession session = service.Session;
        if (session == null || (!session.HasAccessToken && !session.CanRefresh)) {
            RefreshOgsAccountFromSession();
            SetSaveTip("尚未登录 OGS");
            return;
        }

        isOgsProfileRefreshRunning = true;
        SetOgsButtonsInteractable(false);
        SetOgsState(UserInfoPopupUI.SrOgsAccountState.Loading);
        SetText(binder.txt_ogs_loading, "正在刷新 OGS 用户信息...");
        SetSaveTip("正在刷新 OGS 用户信息...");

        try {
            if ((!session.HasAccessToken || session.IsExpired) && session.CanRefresh) {
                OgsConnectionResult tokenResult = await service.RefreshTokenAsync(OgsConnectionConfig.DefaultClientId);
                if (!tokenResult.success) {
                    SetOgsError(tokenResult.message);
                    SetSaveTip($"OGS 刷新失败：{tokenResult.message}");
                    return;
                }
            }

            OgsConnectionResult result = await service.RefreshCurrentUserAsync();
            if (!result.success) {
                SetOgsError(result.message);
                SetSaveTip($"OGS 刷新失败：{result.message}");
                return;
            }

            ApplyOgsSession(service.Session);
            Global.Instance.ogsChallengeInviteCoordinator?.RequestImmediatePoll();
            SetSaveTip("OGS 用户信息已刷新");
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Refresh OGS user info from user info popup failed.", ("err", ex.Message));
            SetOgsError(ex.Message);
            SetSaveTip($"OGS 刷新失败：{ex.Message}");
        }
        finally {
            isOgsProfileRefreshRunning = false;
            SetOgsButtonsInteractable(true);
        }
    }

    private void OnClickBtnLogoutOgs()
    {
        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null) {
            SetOgsError("OGS 登录服务不可用");
            SetSaveTip("OGS 登录服务不可用");
            return;
        }

        service.Logout();
        RefreshOgsAccountFromSession();
        SetSaveTip("已退出 OGS");
    }

    private void RefreshOgsAccountFromSession()
    {
        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasSession) {
            ApplyOgsLoggedOut();
            return;
        }

        ApplyOgsSession(service.Session);
        Global.Instance.ogsChallengeInviteCoordinator?.RequestImmediatePoll();
    }

    private void ApplyOgsLoggedOut()
    {
        ogsAvatarImage?.Clear();
        SetOgsState(UserInfoPopupUI.SrOgsAccountState.LoggedOut);
        SetText(binder.txt_ogs_username, "尚未登录 OGS");
        SetText(binder.txt_ogs_id, "OGS ID: --");
        SetText(binder.txt_ogs_country, "地区: --");
        SetText(binder.txt_ogs_registered, "注册: --");
        SetText(binder.txt_ogs_tags, "标签: --");
        SetText(binder.txt_ogs_about, "简介: --");
        SetText(binder.txt_ogs_rating_overall, "综合段级: --");
        SetText(binder.txt_ogs_ranking, "OGS 段级: --");
        SetText(binder.txt_ogs_rating_19, "19路段级: --");
        SetText(binder.txt_ogs_rating_13, "13路段级: --");
        SetText(binder.txt_ogs_rating_9, "9路段级: --");
        SetText(binder.txt_ogs_friend_summary, "登录后可使用 OGS 好友入口");
    }

    private void ApplyOgsSession(OgsSession session)
    {
        if (session == null || (!session.HasAccessToken && !session.CanRefresh)) {
            ApplyOgsLoggedOut();
            return;
        }

        SetOgsState(UserInfoPopupUI.SrOgsAccountState.LoggedIn);
        SetText(binder.txt_ogs_username, DisplayValue(session.DisplayName, "OGS 用户"));
        SetText(binder.txt_ogs_id, $"OGS ID: {DisplayValue(session.userId)}");
        SetText(binder.txt_ogs_country, $"地区: {DisplayValue(session.country)}");
        SetText(binder.txt_ogs_registered, $"注册: {DisplayValue(session.registeredAt)}");
        SetText(binder.txt_ogs_tags, $"标签: {DisplayValue(session.tags)}");
        SetText(binder.txt_ogs_about, $"简介: {DisplayValue(session.about)}");
        SetText(binder.txt_ogs_rating_overall, $"综合段级: {DisplayValue(session.ratingOverall)}");
        SetText(binder.txt_ogs_ranking, $"OGS 段级: {DisplayValue(session.ranking)}");
        SetText(binder.txt_ogs_rating_19, $"19路段级: {DisplayValue(session.rating19)}");
        SetText(binder.txt_ogs_rating_13, $"13路段级: {DisplayValue(session.rating13)}");
        SetText(binder.txt_ogs_rating_9, $"9路段级: {DisplayValue(session.rating9)}");
        SetText(binder.txt_ogs_friend_summary, "好友列表入口已预留");
        ogsAvatarImage?.Load(session.avatarUrl);
    }

    private void SetOgsError(string message)
    {
        ogsAvatarImage?.Clear();
        SetText(binder.txt_ogs_error, string.IsNullOrWhiteSpace(message) ? "OGS 信息读取失败" : message);
        SetOgsState(UserInfoPopupUI.SrOgsAccountState.Error);
    }

    private void SetOgsState(UserInfoPopupUI.SrOgsAccountState state)
    {
        if (binder.sr_ogs_account != null) {
            binder.SetSrOgsAccountState(state, true);
        }
    }

    private void SetOgsButtonsInteractable(bool interactable)
    {
        if (binder.btn_login_ogs != null) {
            binder.btn_login_ogs.interactable = interactable;
        }
        if (binder.btn_ogs_refresh != null) {
            binder.btn_ogs_refresh.interactable = interactable;
        }
        if (binder.btn_ogs_logout != null) {
            binder.btn_ogs_logout.interactable = interactable;
        }
        if (binder.btn_ogs_retry != null) {
            binder.btn_ogs_retry.interactable = interactable;
        }
    }

    private void SetOgsFriendRedDotVisible(bool visible)
    {
        if (binder.red_dot_ogs_friends != null) {
            binder.red_dot_ogs_friends.gameObject.SetActive(visible);
        }
    }

    private void OnOgsFriendInvitationCountChanged(OnOgsFriendInvitationCountChanged evt)
    {
        SetOgsFriendRedDotVisible(evt != null && evt.count > 0);
    }

    private void ApplyCurrentFriendInvitationBadge()
    {
        OgsConnectionService service = Global.Instance.ogsConnectionService;
        SetOgsFriendRedDotVisible(service != null && service.HasSession && service.FriendInvitationCount > 0);
    }

    private static string DisplayValue(string value, string fallback = "--")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value ?? string.Empty;
        }
    }

    private void ApplyCurrentLayoutState(bool force)
    {
        if (binder.sr_platform == null) {
            return;
        }

        RectTransform layoutRoot = binder.panel_root != null ? binder.panel_root : rectTransform;
        bool isPortrait = UIUtils.IsPortrait(layoutRoot);
        if (!force && hasAppliedLayoutState && isPortrait == lastPortraitLayout) {
            return;
        }

        binder.SetSrPlatformState(isPortrait ? UserInfoPopupUI.SrPlatformState.Portrait : UserInfoPopupUI.SrPlatformState.Landscape, force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;

        if (!force) {
            SetRecentReplaysSummary();
        }
    }

    private void RefreshReplayItems()
    {
        replayItems.Clear();
        replayLoadFailed = !DuelReplayIndexFile.TryLoadItems(out List<DuelReplayIndexItem> items);
        if (!replayLoadFailed && items != null) {
            foreach (DuelReplayIndexItem item in items) {
                if (ShouldShowReplayItem(item)) {
                    replayItems.Add(item);
                }
            }
        }

        replayPageIndex = 0;
        ClearReplayItemWidgets();
        SetReplayEmptyVisible(false);
        SetReplayPageText("0 / 0");
        SetReplayButtonState(false, false);
        SetRecentReplaysSummary();
    }

    private void SetRecentReplaysSummary()
    {
        if (binder.txt_recent_replays_summary == null) {
            return;
        }

        if (replayLoadFailed) {
            binder.txt_recent_replays_summary.text = "最近对局读取失败";
            return;
        }

        binder.txt_recent_replays_summary.text = replayItems.Count <= 0
            ? "暂无本地最近对局"
            : $"共有 {replayItems.Count} 条本地最近对局";
    }

    private void RefreshReplayPage()
    {
        ClearReplayItemWidgets();

        Canvas.ForceUpdateCanvases();
        int itemsPerPage = GetReplayItemsPerPage();
        int pageCount = GetReplayPageCount(itemsPerPage);
        if (pageCount == 0) {
            SetReplayEmptyText(replayLoadFailed ? "最近对局读取失败" : "暂无可复盘对局");
            SetReplayPageText("0 / 0");
            SetReplayButtonState(false, false);
            return;
        }

        replayPageIndex = Mathf.Clamp(replayPageIndex, 0, pageCount - 1);
        int startIndex = replayPageIndex * itemsPerPage;
        int endIndex = Mathf.Min(startIndex + itemsPerPage, replayItems.Count);

        bool hasCreateFailed = false;
        for (int i = startIndex; i < endIndex; i++) {
            ReplayArchiveItemWidget itemWidget = CreateReplayItemWidget();
            if (itemWidget == null) {
                hasCreateFailed = true;
                continue;
            }

            itemWidget.SetData(replayItems[i], OnClickReplayItem);
            replayItemWidgets.Add(itemWidget);
        }

        if (replayItemWidgets.Count == 0) {
            SetReplayEmptyText(hasCreateFailed ? "最近对局列表加载失败" : "暂无可复盘对局");
        } else {
            SetReplayEmptyVisible(false);
        }
        SetReplayPageText($"{replayPageIndex + 1} / {pageCount}");
        SetReplayButtonState(replayPageIndex > 0, replayPageIndex < pageCount - 1);
    }

    private bool ShouldShowReplayItem(DuelReplayIndexItem item)
    {
        return item != null &&
            item.isArchived &&
            !string.IsNullOrEmpty(item.gameId);
    }

    private ReplayArchiveItemWidget CreateReplayItemWidget()
    {
        if (binder.content_replay_list == null) {
            return null;
        }

        GameObject itemGO = Global.Instance.resourceManager.LoadGamePrefab(
            UIUtils.GetWidgetPrefabPath(UIWidget.GetWidgetName<ReplayArchiveItemWidget>()));
        if (itemGO == null) {
            return null;
        }

        ReplayArchiveItemWidget itemWidget = UIWidget.CreateWidgetInstance<ReplayArchiveItemWidget>(this);
        itemWidget.OnUnityResourceLoaded(itemGO);
        itemWidget.transform.SetParent(binder.content_replay_list, false);
        return itemWidget;
    }

    private void ClearReplayItemWidgets()
    {
        foreach (ReplayArchiveItemWidget itemWidget in replayItemWidgets) {
            if (itemWidget != null && itemWidget.isLoaded && itemWidget.gameObject != null) {
                itemWidget.CloseWidget();
                GameObject.Destroy(itemWidget.gameObject);
            }
        }

        replayItemWidgets.Clear();
    }

    private int GetReplayItemsPerPage()
    {
        return UIUtils.GetVisibleListItemCount(
            binder.content_replay_list,
            ReplayArchiveItemWidget.ItemHeight,
            ReplayArchiveItemWidget.ItemSpacing);
    }

    private int GetReplayPageCount(int itemsPerPage)
    {
        if (replayItems.Count <= 0) {
            return 0;
        }

        return (replayItems.Count + itemsPerPage - 1) / itemsPerPage;
    }

    private void SetReplayEmptyVisible(bool isVisible)
    {
        if (binder.txt_replay_empty != null) {
            binder.txt_replay_empty.gameObject.SetActive(isVisible);
        }
    }

    private void SetReplayEmptyText(string text)
    {
        if (binder.txt_replay_empty != null) {
            binder.txt_replay_empty.text = text ?? string.Empty;
        }

        SetReplayEmptyVisible(true);
    }

    private void SetReplayPageText(string text)
    {
        if (binder.txt_replay_page != null) {
            binder.txt_replay_page.text = text;
        }
    }

    private void SetReplayButtonState(bool canPrev, bool canNext)
    {
        if (binder.btn_replay_prev != null) {
            binder.btn_replay_prev.interactable = canPrev;
        }
        if (binder.btn_replay_next != null) {
            binder.btn_replay_next.interactable = canNext;
        }
    }

    private void OnClickBtnReplayPrev()
    {
        if (replayPageIndex <= 0) {
            return;
        }

        replayPageIndex -= 1;
        RefreshReplayPage();
    }

    private void OnClickBtnReplayNext()
    {
        if (replayPageIndex >= GetReplayPageCount(GetReplayItemsPerPage()) - 1) {
            return;
        }

        replayPageIndex += 1;
        RefreshReplayPage();
    }

    private void OnClickReplayItem(DuelReplayIndexItem item)
    {
        if (item == null) {
            return;
        }

        ClosePage();
        SceneCreateParams sceneCreateParams = new SceneCreateParams
        {
            replayGameId = item.gameId,
        };
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.REPLAY_SCENE_TYPE_ID, sceneCreateParams);
    }
}
