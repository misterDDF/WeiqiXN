using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OgsFriendListPopup : UIPageWithBinder<OgsFriendListPopupUI>
{
    private const int MaxItemsPerPage = 6;
    private const float AutoRefreshIntervalSeconds = 5f;

    private readonly List<OgsFriendListItem> friendItems = new List<OgsFriendListItem>();
    private readonly List<OgsFriendItemWidget> itemWidgets = new List<OgsFriendItemWidget>();
    private int pageIndex;
    private float nextAutoRefreshTime;
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

        if (ApplyCurrentLayoutState(false) && friendItems.Count > 0) {
            RefreshPage();
        }
        if (Time.unscaledTime >= nextAutoRefreshTime) {
            nextAutoRefreshTime = Time.unscaledTime + AutoRefreshIntervalSeconds;
            RefreshFriendList(false);
        }
    }

    protected override void OnClose()
    {
        ClearItemWidgets();
        base.OnClose();
    }

    private void RefreshFriendList(bool resetPage = true)
    {
        friendItems.Clear();
        if (resetPage) {
            pageIndex = 0;
        }
        ClearItemWidgets();

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasSession) {
            SetState(OgsFriendListPopupUI.SrOgsFriendState.NotLoggedIn);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            return;
        }

        SetText(binder.txt_empty, "OGS 好友列表接口待确认，当前版本暂不展示好友数据");
        SetState(OgsFriendListPopupUI.SrOgsFriendState.Empty);
        SetText(binder.txt_page, "0 / 0");
        SetPageButtons(false, false);
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
        int startIndex = pageIndex * itemsPerPage;
        int endIndex = Mathf.Min(startIndex + itemsPerPage, friendItems.Count);
        for (int i = startIndex; i < endIndex; i++) {
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
        if (friendItems.Count <= 0) {
            return 0;
        }

        itemsPerPage = Mathf.Max(1, itemsPerPage);
        return (friendItems.Count + itemsPerPage - 1) / itemsPerPage;
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
    }

    private void OnClickBtnPrevPage()
    {
        if (pageIndex <= 0) {
            return;
        }

        pageIndex -= 1;
        RefreshPage();
    }

    private void OnClickBtnNextPage()
    {
        if (pageIndex >= GetPageCount(GetItemsPerPage()) - 1) {
            return;
        }

        pageIndex += 1;
        RefreshPage();
    }

    private void OnClickFriendItem(OgsFriendListItem item)
    {
        SetText(binder.txt_empty, item == null ? string.Empty : item.username);
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

    private static void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }
}
