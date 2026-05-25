using System.Collections.Generic;
using UnityEngine;

public class UserInfoPopup : UIPageWithBinder<UserInfoPopupUI>
{
    private const int MaxReplayItemsPerPage = 7;

    private readonly List<DuelReplayIndexItem> replayItems = new List<DuelReplayIndexItem>();
    private readonly List<ReplayArchiveItemWidget> replayItemWidgets = new List<ReplayArchiveItemWidget>();
    private int replayPageIndex;
    private bool replayLoadFailed;

    public override string pageName => UIPage.GetPageName<UserInfoPopup>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        binder.btn_close.onClick.AddListener(OnClickBtnClose);
        if (binder.btn_edit_name != null) {
            binder.btn_edit_name.onClick.AddListener(OnClickBtnEditName);
        }
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

        RefreshUserInfo();
        RefreshReplayItems();
        SetSaveTip(string.Empty);
    }

    protected override void OnClose()
    {
        ClearReplayItemWidgets();
        base.OnClose();
    }

    public void OnClickBtnClose()
    {
        ClosePage();
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
        RefreshReplayPage();
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
        if (binder.content_replay_list == null) {
            return MaxReplayItemsPerPage;
        }

        float contentHeight = binder.content_replay_list.rect.height;
        if (contentHeight <= 0f) {
            return MaxReplayItemsPerPage;
        }

        int visibleCount = Mathf.FloorToInt(
            (contentHeight + ReplayArchiveItemWidget.ItemSpacing) /
            (ReplayArchiveItemWidget.ItemHeight + ReplayArchiveItemWidget.ItemSpacing));
        return Mathf.Clamp(visibleCount, 1, MaxReplayItemsPerPage);
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

        SetSaveTip("复盘浏览稍后开放");
    }
}
