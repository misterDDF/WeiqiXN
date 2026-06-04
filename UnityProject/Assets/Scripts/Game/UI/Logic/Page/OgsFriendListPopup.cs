using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XNClient.Logger;

public class OgsFriendListPopup : UIPageWithBinder<OgsFriendListPopupUI>
{
    private const int MaxItemsPerPage = 6;
    private const float AutoRefreshIntervalSeconds = 5f;
    private const string FriendListButtonText = "好友列表";
    private const string FriendRequestsButtonText = "好友申请";

    private static OgsFriendListPopup openedPopup;

    private readonly List<OgsFriendListItem> friendItems = new List<OgsFriendListItem>();
    private readonly List<OgsFriendInvitationItem> invitationItems = new List<OgsFriendInvitationItem>();
    private readonly List<OgsFriendItemWidget> itemWidgets = new List<OgsFriendItemWidget>();
    private int pageIndex;
    private int friendTotalCount;
    private int pendingInvitationCount;
    private int lastItemsPerPage = -1;
    private float nextAutoRefreshTime;
    private int refreshVersion;
    private bool isRefreshRunning;
    private bool isInvitationMode;
    private bool isInvitationBadgeRunning;
    private bool isFriendRequestRunning;
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;

    public override string pageName => UIPage.GetPageName<OgsFriendListPopup>();

    private int CurrentItemCount => isInvitationMode ? invitationItems.Count : friendItems.Count;

    public static void NotifyFriendDeleted()
    {
        openedPopup?.RefreshFriendList(false, false);
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        ApplyCurrentLayoutState(true);
        AddButtonListener(binder.btn_close, OnClickBtnClose);
        AddButtonListener(binder.btn_refresh, OnClickBtnRetry);
        AddButtonListener(binder.btn_add_friend, OnClickBtnAddFriend);
        AddButtonListener(binder.btn_friend_requests, OnClickBtnFriendRequests);
        AddButtonListener(binder.btn_retry, OnClickBtnRetry);
        AddButtonListener(binder.btn_login, OnClickBtnLogin);
        AddButtonListener(binder.btn_prev_page, OnClickBtnPrevPage);
        AddButtonListener(binder.btn_next_page, OnClickBtnNextPage);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        openedPopup = this;
        isInvitationMode = false;
        ApplyCurrentLayoutState(false);
        nextAutoRefreshTime = Time.unscaledTime + AutoRefreshIntervalSeconds;
        SetFriendRequestsButtonText();
        RefreshList();
        RefreshInvitationBadge();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        bool layoutChanged = ApplyCurrentLayoutState(false);
        int itemsPerPage = GetItemsPerPage();
        if (layoutChanged && CurrentItemCount > 0) {
            if (itemsPerPage != lastItemsPerPage) {
                RefreshList(false, false);
            } else {
                RefreshPage();
            }
        }
        if (isVisible && Time.unscaledTime >= nextAutoRefreshTime) {
            nextAutoRefreshTime = Time.unscaledTime + AutoRefreshIntervalSeconds;
            RefreshList(false, CurrentItemCount <= 0);
        }
    }

    protected override void OnClose()
    {
        if (openedPopup == this) {
            openedPopup = null;
        }
        refreshVersion += 1;
        isRefreshRunning = false;
        ClearItemWidgets();
        base.OnClose();
    }

    private void RefreshList(bool resetPage = true, bool showLoading = true)
    {
        if (isInvitationMode) {
            RefreshInvitationList(resetPage, showLoading);
        } else {
            RefreshFriendList(resetPage, showLoading);
        }
    }

    private async void RefreshFriendList(bool resetPage = true, bool showLoading = true)
    {
        if (isRefreshRunning) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasSession) {
            friendItems.Clear();
            invitationItems.Clear();
            friendTotalCount = 0;
            pendingInvitationCount = 0;
            if (resetPage) {
                pageIndex = 0;
            }
            ClearItemWidgets();
            SetState(OgsFriendListPopupUI.SrOgsFriendState.NotLoggedIn);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            SetFriendRequestsButtonText();
            return;
        }

        int currentVersion = ++refreshVersion;
        isRefreshRunning = true;
        if (resetPage) {
            pageIndex = 0;
        }
        int itemsPerPage = GetItemsPerPage();
        lastItemsPerPage = itemsPerPage;
        if (showLoading) {
            ClearItemWidgets();
            SetText(binder.txt_loading, "正在读取 OGS 好友...");
            SetState(OgsFriendListPopupUI.SrOgsFriendState.Loading);
            SetPageButtons(false, false);
        }

        try {
            OgsFriendListResult result = await service.RequestFriendListAsync(pageIndex + 1, itemsPerPage);
            if (currentVersion != refreshVersion) {
                return;
            }

            if (!result.success) {
                if (friendItems.Count > 0 && !showLoading) {
                    XNLogger.LogWarn("OGS friend auto refresh failed, keeping current list.", ("message", result.message));
                    return;
                }

                friendItems.Clear();
                friendTotalCount = 0;
                ClearItemWidgets();
                SetText(binder.txt_error, string.IsNullOrWhiteSpace(result.message) ? "OGS 好友列表读取失败" : result.message);
                SetState(OgsFriendListPopupUI.SrOgsFriendState.Error);
                SetText(binder.txt_page, "0 / 0");
                SetPageButtons(false, false);
                return;
            }

            friendItems.Clear();
            if (result.friends != null) {
                friendItems.AddRange(result.friends);
            }
            friendTotalCount = Mathf.Max(result.totalCount, friendItems.Count);
            RefreshPage();
            RefreshInvitationBadge();
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Refresh OGS friend list from popup failed.", ("err", ex.Message));
            if (currentVersion != refreshVersion) {
                return;
            }

            friendItems.Clear();
            friendTotalCount = 0;
            ClearItemWidgets();
            SetText(binder.txt_error, ex.Message);
            SetState(OgsFriendListPopupUI.SrOgsFriendState.Error);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
        }
        finally {
            if (currentVersion == refreshVersion) {
                isRefreshRunning = false;
                nextAutoRefreshTime = Time.unscaledTime + AutoRefreshIntervalSeconds;
            }
        }
    }

    private async void RefreshInvitationList(bool resetPage = true, bool showLoading = true)
    {
        if (isRefreshRunning) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasSession) {
            friendItems.Clear();
            invitationItems.Clear();
            friendTotalCount = 0;
            pendingInvitationCount = 0;
            if (resetPage) {
                pageIndex = 0;
            }
            ClearItemWidgets();
            SetState(OgsFriendListPopupUI.SrOgsFriendState.NotLoggedIn);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            SetFriendRequestsButtonText();
            return;
        }

        int currentVersion = ++refreshVersion;
        isRefreshRunning = true;
        if (resetPage) {
            pageIndex = 0;
        }
        int itemsPerPage = GetItemsPerPage();
        lastItemsPerPage = itemsPerPage;
        if (showLoading) {
            ClearItemWidgets();
            SetText(binder.txt_loading, "正在读取 OGS 好友申请...");
            SetState(OgsFriendListPopupUI.SrOgsFriendState.Loading);
            SetPageButtons(false, false);
        }

        try {
            OgsFriendInvitationListResult result = await service.RequestFriendInvitationsAsync();
            if (currentVersion != refreshVersion) {
                return;
            }

            if (!result.success) {
                if (invitationItems.Count > 0 && !showLoading) {
                    XNLogger.LogWarn("OGS friend invitation auto refresh failed, keeping current list.", ("message", result.message));
                    return;
                }

                invitationItems.Clear();
                friendTotalCount = 0;
                ClearItemWidgets();
                SetText(binder.txt_error, string.IsNullOrWhiteSpace(result.message) ? "OGS 好友申请读取失败" : result.message);
                SetState(OgsFriendListPopupUI.SrOgsFriendState.Error);
                SetText(binder.txt_page, "0 / 0");
                SetPageButtons(false, false);
                return;
            }

            invitationItems.Clear();
            if (result.invitations != null) {
                invitationItems.AddRange(result.invitations);
            }
            friendTotalCount = invitationItems.Count;
            pendingInvitationCount = invitationItems.Count;
            SetFriendRequestsButtonText();
            RefreshPage();
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Refresh OGS friend invitation list from popup failed.", ("err", ex.Message));
            if (currentVersion != refreshVersion) {
                return;
            }

            invitationItems.Clear();
            friendTotalCount = 0;
            ClearItemWidgets();
            SetText(binder.txt_error, ex.Message);
            SetState(OgsFriendListPopupUI.SrOgsFriendState.Error);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
        }
        finally {
            if (currentVersion == refreshVersion) {
                isRefreshRunning = false;
                nextAutoRefreshTime = Time.unscaledTime + AutoRefreshIntervalSeconds;
            }
        }
    }

    private async void RefreshInvitationBadge()
    {
        if (isInvitationMode || isInvitationBadgeRunning) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasSession) {
            invitationItems.Clear();
            pendingInvitationCount = 0;
            SetFriendRequestsButtonText();
            return;
        }

        isInvitationBadgeRunning = true;
        try {
            OgsFriendInvitationListResult result = await service.RequestFriendInvitationsAsync();
            if (isInvitationMode) {
                return;
            }

            if (!result.success) {
                XNLogger.LogWarn("OGS friend invitation badge refresh failed.", ("message", result.message));
                return;
            }

            invitationItems.Clear();
            if (result.invitations != null) {
                invitationItems.AddRange(result.invitations);
            }
            pendingInvitationCount = invitationItems.Count;
            SetFriendRequestsButtonText();
        }
        catch (System.Exception ex) {
            XNLogger.LogWarn("Refresh OGS friend invitation badge failed.", ("err", ex.Message));
        }
        finally {
            isInvitationBadgeRunning = false;
        }
    }

    private void RefreshPage()
    {
        ClearItemWidgets();

        int itemsPerPage = GetItemsPerPage();
        int pageCount = GetPageCount(itemsPerPage);
        if (pageCount <= 0) {
            SetText(binder.txt_empty, isInvitationMode ? "暂无 OGS 好友申请" : "暂无 OGS 好友");
            SetState(OgsFriendListPopupUI.SrOgsFriendState.Empty);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            return;
        }

        pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
        int startIndex = isInvitationMode ? pageIndex * itemsPerPage : 0;
        int endIndex = Mathf.Min(startIndex + itemsPerPage, CurrentItemCount);
        for (int i = startIndex; i < endIndex; i++) {
            OgsFriendItemWidget itemWidget = CreateItemWidget();
            if (itemWidget == null) {
                continue;
            }

            itemWidget.SetData(GetDisplayItem(i), OnClickFriendItem);
            itemWidgets.Add(itemWidget);
        }

        if (itemWidgets.Count <= 0) {
            SetText(binder.txt_error, isInvitationMode ? "好友申请列表控件加载失败" : "好友列表控件加载失败");
            SetState(OgsFriendListPopupUI.SrOgsFriendState.Error);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            return;
        }

        SetState(OgsFriendListPopupUI.SrOgsFriendState.Content);
        SetText(binder.txt_page, $"{pageIndex + 1} / {pageCount}");
        SetPageButtons(pageIndex > 0, pageIndex < pageCount - 1);
    }

    private OgsFriendItemWidget CreateItemWidget()
    {
        if (binder.content_friend_list == null) {
            return null;
        }

        GameObject itemGO = Global.Instance.resourceManager.LoadGamePrefab(
            UIUtils.GetWidgetPrefabPath(UIWidget.GetWidgetName<OgsFriendItemWidget>()));
        if (itemGO == null) {
            return null;
        }

        OgsFriendItemWidget itemWidget = UIWidget.CreateWidgetInstance<OgsFriendItemWidget>(this);
        itemWidget.OnUnityResourceLoaded(itemGO);
        itemWidget.transform.SetParent(binder.content_friend_list, false);
        return itemWidget;
    }

    private void ClearItemWidgets()
    {
        foreach (OgsFriendItemWidget itemWidget in itemWidgets) {
            if (itemWidget != null && itemWidget.isLoaded && itemWidget.gameObject != null) {
                itemWidget.CloseWidget();
                GameObject.Destroy(itemWidget.gameObject);
            }
        }

        itemWidgets.Clear();
    }

    private int GetItemsPerPage()
    {
        if (binder.content_friend_list == null) {
            return MaxItemsPerPage;
        }

        Canvas.ForceUpdateCanvases();
        float contentHeight = binder.content_friend_list.rect.height;
        if (contentHeight <= 0f) {
            return MaxItemsPerPage;
        }

        int visibleCount = Mathf.FloorToInt(
            (contentHeight + OgsFriendItemWidget.ItemSpacing) /
            (OgsFriendItemWidget.ItemHeight + OgsFriendItemWidget.ItemSpacing));
        return Mathf.Clamp(visibleCount, 1, MaxItemsPerPage);
    }

    private int GetPageCount(int itemsPerPage)
    {
        int totalCount = Mathf.Max(friendTotalCount, CurrentItemCount);
        if (totalCount <= 0) {
            return 0;
        }

        itemsPerPage = Mathf.Max(1, itemsPerPage);
        return (totalCount + itemsPerPage - 1) / itemsPerPage;
    }

    private void OnClickBtnClose()
    {
        ClosePage();
    }

    private void OnClickBtnRetry()
    {
        RefreshList();
    }

    private void OnClickBtnAddFriend()
    {
        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasSession) {
            ConfirmPopup.ShowTip("添加 OGS 好友", "请先登录 OGS。", null, "确定");
            return;
        }

        ConfirmPopup.ShowInput(
            "添加 OGS 好友",
            "请输入对方 OGS ID",
            string.Empty,
            SendOgsFriendRequest,
            null,
            "发送",
            "取消");
    }

    private void OnClickBtnFriendRequests()
    {
        isInvitationMode = !isInvitationMode;
        pageIndex = 0;
        if (isRefreshRunning) {
            refreshVersion += 1;
            isRefreshRunning = false;
        }
        SetFriendRequestsButtonText();
        RefreshList();
    }

    private void OnClickBtnLogin()
    {
        ClosePage();
        Global.Instance.uiManager.ShowPage<UserInfoPopup>();
    }

    private void OnClickBtnPrevPage()
    {
        if (pageIndex <= 0) {
            return;
        }

        pageIndex -= 1;
        RefreshList(false);
    }

    private void OnClickBtnNextPage()
    {
        if (pageIndex >= GetPageCount(GetItemsPerPage()) - 1) {
            return;
        }

        pageIndex += 1;
        RefreshList(false);
    }

    private void OnClickFriendItem(OgsFriendListItem item)
    {
        if (item == null) {
            return;
        }

        if (isInvitationMode) {
            OgsFriendInvitationItem invitation = FindInvitation(item);
            if (invitation == null) {
                return;
            }

            string inviteUsername = string.IsNullOrWhiteSpace(invitation.FromUsername) ? "OGS 用户" : invitation.FromUsername.Trim();
            ConfirmPopup.Show(
                "好友申请",
                $"{inviteUsername}申请添加好友",
                () => RespondInvitation(invitation, true),
                () => RespondInvitation(invitation, false),
                "同意",
                "拒绝");
            return;
        }

        OgsFriendProfilePopup.Show(item);
    }

    private async void SendOgsFriendRequest(string rawPlayerId)
    {
        if (isFriendRequestRunning) {
            return;
        }

        if (!int.TryParse((rawPlayerId ?? string.Empty).Trim(), out int playerId) || playerId <= 0) {
            ConfirmPopup.ShowTip("添加 OGS 好友", "请输入有效的 OGS ID。", null, "确定");
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null) {
            ConfirmPopup.ShowTip("添加 OGS 好友", "OGS 服务不可用。", null, "确定");
            return;
        }

        isFriendRequestRunning = true;
        try {
            OgsConnectionResult result = await service.SendFriendRequestAsync(playerId);
            if (!result.success) {
                ConfirmPopup.ShowTip("添加 OGS 好友失败", result.message, null, "确定");
                return;
            }

            ConfirmPopup.ShowTip("添加 OGS 好友", "已发送好友申请", null, "确定");
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Send OGS friend request from friend popup failed.", ("err", ex.Message));
            ConfirmPopup.ShowTip("添加 OGS 好友失败", ex.Message, null, "确定");
        }
        finally {
            isFriendRequestRunning = false;
        }
    }

    private async void RespondInvitation(OgsFriendInvitationItem invitation, bool accept)
    {
        if (invitation == null || !int.TryParse(invitation.FromUserId, out int fromUserId) || fromUserId <= 0) {
            ConfirmPopup.ShowTip("好友申请", "好友申请用户 ID 无效。", null, "确定");
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null) {
            ConfirmPopup.ShowTip("好友申请", "OGS 服务不可用。", null, "确定");
            return;
        }

        OgsConnectionResult result = await service.RespondFriendInvitationAsync(fromUserId, accept);
        if (!result.success) {
            ConfirmPopup.ShowTip("好友申请处理失败", result.message, null, "确定");
            return;
        }

        invitationItems.Remove(invitation);
        friendTotalCount = invitationItems.Count;
        pendingInvitationCount = invitationItems.Count;
        SetFriendRequestsButtonText();
        service.EmitFriendInvitationCountChanged(pendingInvitationCount);
        RefreshPage();
        ConfirmPopup.ShowTip("好友申请", accept ? "已同意好友申请" : "已拒绝好友申请", null, "确定");
    }

    private OgsFriendListItem GetDisplayItem(int index)
    {
        if (isInvitationMode) {
            OgsFriendInvitationItem invitation = index >= 0 && index < invitationItems.Count ? invitationItems[index] : null;
            OgsFriendListItem item = invitation?.fromUser;
            if (item != null && string.IsNullOrWhiteSpace(item.statusText)) {
                item.statusText = "待处理";
            }
            return item;
        }

        return index >= 0 && index < friendItems.Count ? friendItems[index] : null;
    }

    private OgsFriendInvitationItem FindInvitation(OgsFriendListItem item)
    {
        if (item == null) {
            return null;
        }

        foreach (OgsFriendInvitationItem invitation in invitationItems) {
            if (invitation?.fromUser == item) {
                return invitation;
            }
        }

        return null;
    }

    private bool ApplyCurrentLayoutState(bool force)
    {
        if (binder.sr_platform == null) {
            return false;
        }

        RectTransform layoutRoot = binder.panel_root != null ? binder.panel_root : rectTransform;
        bool isPortrait = UIUtils.IsPortrait(layoutRoot);
        if (!force && hasAppliedLayoutState && isPortrait == lastPortraitLayout) {
            return false;
        }

        binder.SetSrPlatformState(isPortrait
            ? OgsFriendListPopupUI.SrPlatformState.Portrait
            : OgsFriendListPopupUI.SrPlatformState.Landscape, force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;
        return true;
    }

    private void SetState(OgsFriendListPopupUI.SrOgsFriendState state)
    {
        binder.SetSrOgsFriendState(state, true);
    }

    private void SetPageButtons(bool canPrev, bool canNext)
    {
        SetInteractable(binder.btn_prev_page, canPrev);
        SetInteractable(binder.btn_next_page, canNext);
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value ?? string.Empty;
        }
    }

    private void SetFriendRequestsButtonText()
    {
        TextMeshProUGUI label = binder.btn_friend_requests != null
            ? binder.btn_friend_requests.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        if (label == null) {
            return;
        }

        if (isInvitationMode) {
            label.text = FriendListButtonText;
            return;
        }

        label.text = pendingInvitationCount > 0
            ? $"{FriendRequestsButtonText}({pendingInvitationCount})"
            : FriendRequestsButtonText;
    }

    private static void SetInteractable(Button button, bool interactable)
    {
        if (button != null) {
            button.interactable = interactable;
        }
    }

    private static string DisplayValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
    }

    private static void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }
}
