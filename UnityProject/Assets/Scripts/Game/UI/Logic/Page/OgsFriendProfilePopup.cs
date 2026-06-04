using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OgsFriendProfilePopup : UIPageWithBinder<OgsFriendProfilePopupUI>
{
    private static OgsFriendListItem pendingItem;

    private OgsFriendListItem currentItem;

    public override string pageName => UIPage.GetPageName<OgsFriendProfilePopup>();

    public static void Show(OgsFriendListItem item)
    {
        pendingItem = item;
        Global.Instance.uiManager.ShowPage<OgsFriendProfilePopup>();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        AddButtonListener(binder.btn_close, OnClickClose);
        AddButtonListener(binder.btn_invite_game, OnClickInviteGame);
        AddButtonListener(binder.btn_delete_friend, OnClickDeleteFriend);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        currentItem = pendingItem;
        pendingItem = null;
        ApplyData(currentItem);
    }

    private void ApplyData(OgsFriendListItem item)
    {
        string username = DisplayValue(item?.username, "OGS 好友");

        SetText(binder.txt_username, username);
        SetText(binder.txt_status, DisplayValue(item?.statusText, "状态未知"));
        SetText(binder.txt_user_id, $"OGS ID: {DisplayValue(item?.userId)}");
        SetText(binder.txt_country, $"地区: {DisplayValue(item?.country)}");
        SetText(binder.txt_rating_overall, $"综合评分: {DisplayValue(item?.ratingOverall)}");
        SetText(binder.txt_ranking, $"排名: {DisplayValue(item?.rankingText)}");
        SetText(binder.txt_rating_19, $"19 路: {DisplayValue(item?.rating19)}");
        SetText(binder.txt_rating_13, $"13 路: {DisplayValue(item?.rating13)}");
        SetText(binder.txt_rating_9, $"9 路: {DisplayValue(item?.rating9)}");
        SetText(binder.txt_registered, $"注册时间: {DisplayValue(item?.registeredAt)}");
        SetText(binder.txt_about, $"简介: {DisplayValue(item?.about)}");
        SetText(binder.txt_note, "资料来自 OGS，部分字段可能为空");

        if (binder.img_avatar != null) {
            binder.img_avatar.color = new Color(0.78f, 0.62f, 0.28f, 1f);
        }
    }

    private void OnClickClose()
    {
        ClosePage();
    }

    private void OnClickInviteGame()
    {
        string username = DisplayValue(currentItem?.username, "该好友");
        ConfirmPopup.ShowTip("邀请对局", $"暂未接入向 {username} 发起 OGS 邀请对局的接口。", null, "确定");
    }

    private void OnClickDeleteFriend()
    {
        string username = DisplayValue(currentItem?.username, "该好友");
        ConfirmPopup.ShowTip("删除好友", $"暂未接入从 OGS 好友列表删除 {username} 的接口。", null, "确定");
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

    private static void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }
}
