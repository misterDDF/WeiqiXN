using System;
using UnityEngine;
using UnityEngine.UI;

public class OgsFriendItemWidget : UIWidgetWithBinder<OgsFriendItemWidgetUI>
{
    public const float ItemHeight = 86f;
    public const float ItemSpacing = 8f;

    private static readonly Color AvatarEmptyColor = UIPalette.PaperSunken;

    private OgsFriendListItem item;
    private Action<OgsFriendListItem> clickHandler;
    private Action<OgsFriendListItem, bool> invitationHandler;
    private RemoteImageView avatarImage;

    public override string widgetName => UIWidget.GetWidgetName<OgsFriendItemWidget>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        if (binder.btn_item != null) {
            binder.btn_item.onClick.AddListener(OnClickItem);
        }
        if (binder.btn_profile != null) {
            binder.btn_profile.onClick.AddListener(OnClickItem);
        }
        avatarImage = new RemoteImageView(binder, binder.img_avatar, AvatarEmptyColor);
        binder.btn_accept.onClick.AddListener(OnAcceptInvitation);
        binder.btn_reject.onClick.AddListener(OnRejectInvitation);
    }

    protected override void OnClose()
    {
        avatarImage?.Clear();
        avatarImage = null;

        if (binder != null) {
            if (binder.btn_item != null) {
                binder.btn_item.onClick.RemoveListener(OnClickItem);
            }
            if (binder.btn_profile != null) {
                binder.btn_profile.onClick.RemoveListener(OnClickItem);
            }
        }

        clickHandler = null;
        invitationHandler = null;
        binder.btn_accept.onClick.RemoveListener(OnAcceptInvitation);
        binder.btn_reject.onClick.RemoveListener(OnRejectInvitation);
        item = null;
        base.OnClose();
    }

    public void SetData(OgsFriendListItem data, Action<OgsFriendListItem> onClick, Action<OgsFriendListItem, bool> onInvitation = null)
    {
        item = data;
        clickHandler = onClick;
        invitationHandler = onInvitation;
        binder.sr_row_mode.SetState(onInvitation != null ? "Invitation" : "Normal");

        SetText(binder.txt_username, Display(data?.username, "OGS 好友"));
        SetText(binder.txt_meta, BuildMeta(data));
        SetText(binder.txt_rating, Display(data?.ratingText, "段位/等级未知"));
        SetText(binder.txt_status, Display(data?.statusText, "状态未知"));

        avatarImage?.Load(data?.avatarUrl);

        LayoutElement layoutElement = gameObject != null ? gameObject.GetComponent<LayoutElement>() : null;
        if (layoutElement != null) {
            layoutElement.minHeight = ItemHeight;
            layoutElement.preferredHeight = ItemHeight;
        }
    }

    private static string BuildMeta(OgsFriendListItem data)
    {
        if (data == null) {
            return "OGS ID: -- / 地区: --";
        }

        return $"OGS ID: {Display(data.userId)} / 地区: {Display(data.country)}";
    }

    public void SetInvitationInteractable(bool interactable)
    {
        binder.btn_accept.interactable = interactable;
        binder.btn_reject.interactable = interactable;
    }

    private static string Display(string value, string fallback = "--")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static void SetText(TMPro.TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value ?? string.Empty;
        }
    }

    private void OnClickItem()
    {
        clickHandler?.Invoke(item);
    }

    private void OnAcceptInvitation()
    {
        invitationHandler?.Invoke(item, true);
    }

    private void OnRejectInvitation()
    {
        invitationHandler?.Invoke(item, false);
    }
}
