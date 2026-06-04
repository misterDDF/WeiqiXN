using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XNClient.Logger;

public class OgsFriendListPopup : UIPageWithBinder<OgsFriendListPopupUI>
{
    private const int MaxItemsPerPage = 6;
    private const float AutoRefreshIntervalSeconds = 5f;

    private readonly List<OgsFriendListItem> friendItems = new List<OgsFriendListItem>();
    private readonly List<OgsFriendItemWidget> itemWidgets = new List<OgsFriendItemWidget>();
    private int pageIndex;
    private int friendTotalCount;
    private int lastItemsPerPage = -1;
    private float nextAutoRefreshTime;
    private int refreshVersion;
    private bool isRefreshRunning;
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;

    public override string pageName => UIPage.GetPageName<OgsFriendListPopup>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        ApplyCurrentLayoutState(true);
        AddButtonListener(binder.btn_close, OnClickBtnClose);
        AddButtonListener(binder.btn_retry, OnClickBtnRetry);
        AddButtonListener(binder.btn_login, OnClickBtnLogin);
        AddButtonListener(binder.btn_prev_page, OnClickBtnPrevPage);
        AddButtonListener(binder.btn_next_page, OnClickBtnNextPage);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        ApplyCurrentLayoutState(false);
        nextAutoRefreshTime = Time.unscaledTime + AutoRefreshIntervalSeconds;
        RefreshFriendList();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        bool layoutChanged = ApplyCurrentLayoutState(false);
        int itemsPerPage = GetItemsPerPage();
        if (layoutChanged && friendItems.Count > 0) {
            if (itemsPerPage != lastItemsPerPage) {
                RefreshFriendList(false, false);
            } else {
                RefreshPage();
            }
        }
        if (isVisible && Time.unscaledTime >= nextAutoRefreshTime) {
            nextAutoRefreshTime = Time.unscaledTime + AutoRefreshIntervalSeconds;
            RefreshFriendList(false, friendItems.Count <= 0);
        }
    }

    protected override void OnClose()
    {
        refreshVersion += 1;
        isRefreshRunning = false;
        ClearItemWidgets();
        base.OnClose();
    }

    private async void RefreshFriendList(bool resetPage = true, bool showLoading = true)
    {
        if (isRefreshRunning) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasSession) {
            friendItems.Clear();
            friendTotalCount = 0;
            if (resetPage) {
                pageIndex = 0;
            }
            ClearItemWidgets();
            SetState(OgsFriendListPopupUI.SrOgsFriendState.NotLoggedIn);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
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

    private void RefreshPage()
    {
        ClearItemWidgets();

        int itemsPerPage = GetItemsPerPage();
        int pageCount = GetPageCount(itemsPerPage);
        if (pageCount <= 0) {
            SetText(binder.txt_empty, "暂无 OGS 好友");
            SetState(OgsFriendListPopupUI.SrOgsFriendState.Empty);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            return;
        }

        pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
        int endIndex = Mathf.Min(itemsPerPage, friendItems.Count);
        for (int i = 0; i < endIndex; i++) {
            OgsFriendItemWidget itemWidget = CreateItemWidget();
            if (itemWidget == null) {
                continue;
            }

            itemWidget.SetData(friendItems[i], OnClickFriendItem);
            itemWidgets.Add(itemWidget);
        }

        if (itemWidgets.Count <= 0) {
            SetText(binder.txt_error, "好友列表控件加载失败");
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
        int totalCount = Mathf.Max(friendTotalCount, friendItems.Count);
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
        RefreshFriendList();
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
        RefreshFriendList(false);
    }

    private void OnClickBtnNextPage()
    {
        if (pageIndex >= GetPageCount(GetItemsPerPage()) - 1) {
            return;
        }

        pageIndex += 1;
        RefreshFriendList(false);
    }

    private void OnClickFriendItem(OgsFriendListItem item)
    {
        if (item == null) {
            return;
        }

        OgsFriendProfilePopup.Show(item);
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
