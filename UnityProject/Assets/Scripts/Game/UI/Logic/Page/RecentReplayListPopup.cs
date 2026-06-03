using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RecentReplayListPopup : UIPageWithBinder<RecentReplayListPopupUI>
{
    private const int MaxItemsPerPage = 6;

    private readonly List<DuelReplayIndexItem> replayItems = new List<DuelReplayIndexItem>();
    private readonly List<ReplayArchiveItemWidget> itemWidgets = new List<ReplayArchiveItemWidget>();
    private int pageIndex;
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;

    public override string pageName => UIPage.GetPageName<RecentReplayListPopup>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        ApplyCurrentLayoutState(true);
        AddButtonListener(binder.btn_close, OnClickBtnClose);
        AddButtonListener(binder.btn_retry, OnClickBtnRetry);
        AddButtonListener(binder.btn_prev_page, OnClickBtnPrevPage);
        AddButtonListener(binder.btn_next_page, OnClickBtnNextPage);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        if (ApplyCurrentLayoutState(false) && replayItems.Count > 0) {
            RefreshPage();
        }
        RefreshReplayItems();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ApplyCurrentLayoutState(false);
    }

    protected override void OnClose()
    {
        ClearItemWidgets();
        base.OnClose();
    }

    private void RefreshReplayItems()
    {
        replayItems.Clear();
        pageIndex = 0;
        ClearItemWidgets();
        SetState(RecentReplayListPopupUI.SrRecentReplayState.Loading);
        SetText(binder.txt_loading, "正在读取复盘记录...");

        if (!DuelReplayIndexFile.TryLoadItems(out List<DuelReplayIndexItem> items)) {
            SetText(binder.txt_error, "最近对局读取失败");
            SetState(RecentReplayListPopupUI.SrRecentReplayState.Error);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            return;
        }

        if (items != null) {
            foreach (DuelReplayIndexItem item in items) {
                if (ShouldShowReplayItem(item)) {
                    replayItems.Add(item);
                }
            }
        }

        RefreshPage();
    }

    private void RefreshPage()
    {
        ClearItemWidgets();

        int itemsPerPage = GetItemsPerPage();
        int pageCount = GetPageCount(itemsPerPage);
        if (pageCount <= 0) {
            SetText(binder.txt_empty, "暂无最近对局");
            SetState(RecentReplayListPopupUI.SrRecentReplayState.Empty);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            return;
        }

        pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
        int startIndex = pageIndex * itemsPerPage;
        int endIndex = Mathf.Min(startIndex + itemsPerPage, replayItems.Count);
        for (int i = startIndex; i < endIndex; i++) {
            ReplayArchiveItemWidget itemWidget = CreateItemWidget();
            if (itemWidget == null) {
                continue;
            }

            itemWidget.SetData(replayItems[i], OnClickReplayItem);
            itemWidgets.Add(itemWidget);
        }

        if (itemWidgets.Count <= 0) {
            SetText(binder.txt_error, "最近对局列表控件加载失败");
            SetState(RecentReplayListPopupUI.SrRecentReplayState.Error);
            SetText(binder.txt_page, "0 / 0");
            SetPageButtons(false, false);
            return;
        }

        SetState(RecentReplayListPopupUI.SrRecentReplayState.Content);
        SetText(binder.txt_page, $"{pageIndex + 1} / {pageCount}");
        SetPageButtons(pageIndex > 0, pageIndex < pageCount - 1);
    }

    private static bool ShouldShowReplayItem(DuelReplayIndexItem item)
    {
        return item != null &&
            item.isArchived &&
            !string.IsNullOrEmpty(item.gameId);
    }

    private ReplayArchiveItemWidget CreateItemWidget()
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

    private void ClearItemWidgets()
    {
        foreach (ReplayArchiveItemWidget itemWidget in itemWidgets) {
            if (itemWidget != null && itemWidget.isLoaded && itemWidget.gameObject != null) {
                itemWidget.CloseWidget();
                GameObject.Destroy(itemWidget.gameObject);
            }
        }

        itemWidgets.Clear();
    }

    private int GetItemsPerPage()
    {
        if (binder.content_replay_list == null) {
            return MaxItemsPerPage;
        }

        Canvas.ForceUpdateCanvases();
        float contentHeight = binder.content_replay_list.rect.height;
        if (contentHeight <= 0f) {
            return MaxItemsPerPage;
        }

        int visibleCount = Mathf.FloorToInt(
            (contentHeight + ReplayArchiveItemWidget.ItemSpacing) /
            (ReplayArchiveItemWidget.ItemHeight + ReplayArchiveItemWidget.ItemSpacing));
        return Mathf.Clamp(visibleCount, 1, MaxItemsPerPage);
    }

    private int GetPageCount(int itemsPerPage)
    {
        if (replayItems.Count <= 0) {
            return 0;
        }

        itemsPerPage = Mathf.Max(1, itemsPerPage);
        return (replayItems.Count + itemsPerPage - 1) / itemsPerPage;
    }

    private void OnClickBtnClose()
    {
        ClosePage();
    }

    private void OnClickBtnRetry()
    {
        RefreshReplayItems();
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
            ? RecentReplayListPopupUI.SrPlatformState.Portrait
            : RecentReplayListPopupUI.SrPlatformState.Landscape, force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;
        return true;
    }

    private void SetState(RecentReplayListPopupUI.SrRecentReplayState state)
    {
        binder.SetSrRecentReplayState(state, true);
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
