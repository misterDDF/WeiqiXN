using System;

public class LanRoomItemWidget : UIWidgetWithBinder<LanRoomItemWidgetUI>
{
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
            binder.txt_room.text = room.GetDisplayText();
        }
    }

    private void OnClickRoom()
    {
        clickHandler?.Invoke(room);
    }
}
