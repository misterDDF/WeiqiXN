using System;
using UnityEngine;
using UnityEngine.UI;

public class OgsFriendItemWidget : UIWidgetWithBinder<OgsFriendItemWidgetUI>
{
    public const float ItemHeight = 86f;
    public const float ItemSpacing = 8f;

    private OgsFriendListItem item;
    private Action<OgsFriendListItem> clickHandler;

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
    }

    protected override void OnClose()
    {
        if (binder != null) {
            if (binder.btn_item != null) {
                binder.btn_item.onClick.RemoveListener(OnClickItem);
            }
            if (binder.btn_profile != null) {
                binder.btn_profile.onClick.RemoveListener(OnClickItem);
            }
        }

        clickHandler = null;
        item = null;
        base.OnClose();
    }

    public void SetData(OgsFriendListItem data, Action<OgsFriendListItem> onClick)
    {
        item = data;
        clickHandler = onClick;

        SetText(binder.txt_username, Display(data?.username, "OGS 好友"));
        SetText(binder.txt_meta, BuildMeta(data));
        SetText(binder.txt_rating, Display(data?.ratingText, "段位/等级未知"));
        SetText(binder.txt_status, Display(data?.statusText, "状态未知"));

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
}

public sealed class OgsFriendListItem
{
    public string userId;
    public string username;
    public string country;
    public string ratingText;
    public string statusText;
}
