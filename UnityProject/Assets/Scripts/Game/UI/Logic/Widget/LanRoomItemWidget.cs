using System;
using UnityEngine;
using UnityEngine.UI;

public class LanRoomItemWidget : UIWidgetWithBinder<LanRoomItemWidgetUI>
{
    private const float RoomItemHeight = 104f;

    private LanRoomInfo room;
    private Action<LanRoomInfo> clickHandler;

    public override string widgetName => UIWidget.GetWidgetName<LanRoomItemWidget>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        if (binder.btn_room != null) {
            binder.btn_room.onClick.AddListener(OnClickRoom);
        }
    }

    protected override void OnClose()
    {
        if (binder != null && binder.btn_room != null) {
            binder.btn_room.onClick.RemoveListener(OnClickRoom);
        }

        clickHandler = null;
        base.OnClose();
    }

    public void SetData(LanRoomInfo nextRoom, Action<LanRoomInfo> onClick)
    {
        room = nextRoom;
        clickHandler = onClick;

        if (binder != null && binder.txt_room != null) {
            binder.txt_room.enableWordWrapping = false;
            binder.txt_room.fontSize = 18f;
            binder.txt_room.text = room.GetDisplayText();
        }

        LayoutElement layoutElement = gameObject != null ? gameObject.GetComponent<LayoutElement>() : null;
        if (layoutElement != null) {
            layoutElement.minHeight = RoomItemHeight;
            layoutElement.preferredHeight = RoomItemHeight;
        }

        RectTransform rectTransform = transform as RectTransform;
        if (rectTransform != null) {
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RoomItemHeight);
        }
    }

    private void OnClickRoom()
    {
        clickHandler?.Invoke(room);
    }
}
