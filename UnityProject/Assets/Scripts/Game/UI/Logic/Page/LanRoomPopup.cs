using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LanRoomPopup : UIPageWithBinder<LanRoomPopupUI>
{
    private static readonly List<LanRoomInfo> MockSearchRooms = new List<LanRoomInfo>
    {
        new LanRoomInfo("\u672c\u673a\u6d4b\u8bd5\u623f\u95f4", "192.168.1.12", 1, 2),
        new LanRoomInfo("\u5c40\u57df\u7f51\u623f\u95f4 A", "192.168.1.24", 1, 2),
        new LanRoomInfo("\u5c40\u57df\u7f51\u623f\u95f4 B", "192.168.1.35", 1, 2),
    };

    private readonly List<Button> roomButtons = new List<Button>();
    private readonly List<TextMeshProUGUI> roomTexts = new List<TextMeshProUGUI>();
    private readonly List<LanRoomInfo> visibleRooms = new List<LanRoomInfo>();

    public override string pageName => UIPage.GetPageName<LanRoomPopup>();

    public static void OpenPopup()
    {
        Global.Instance.uiManager.ShowPage<LanRoomPopup>();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        AddButtonListener(binder.btn_create_room, OnClickBtnCreateRoom);
        AddButtonListener(binder.btn_search_room, OnClickBtnSearchRoom);
        AddButtonListener(binder.btn_close, OnClickBtnClose);

        RegisterRoomRow(binder.btn_room_0, binder.txt_room_0, 0);
        RegisterRoomRow(binder.btn_room_1, binder.txt_room_1, 1);
        RegisterRoomRow(binder.btn_room_2, binder.txt_room_2, 2);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        SetStatus("\u8bf7\u9009\u62e9\u521b\u5efa\u623f\u95f4\u6216\u641c\u7d22\u623f\u95f4\u3002");
        RefreshRoomList(null);
    }

    public void OnClickBtnCreateRoom()
    {
        RefreshRoomList(new List<LanRoomInfo>
        {
            new LanRoomInfo("\u6211\u7684\u5c40\u57df\u7f51\u623f\u95f4", "\u672c\u673a", 1, 2),
        });
        SetStatus("\u5df2\u521b\u5efa\u672c\u5730\u623f\u95f4\uff0c\u7b49\u5f85\u540c\u4e00\u5c40\u57df\u7f51\u73a9\u5bb6\u52a0\u5165\u3002");
    }

    public void OnClickBtnSearchRoom()
    {
        RefreshRoomList(MockSearchRooms);
        SetStatus("\u5df2\u5c55\u793a\u641c\u7d22\u5230\u7684\u5c40\u57df\u7f51\u623f\u95f4\u3002");
    }

    public void OnClickBtnClose()
    {
        ClosePage();
    }

    private void RegisterRoomRow(Button button, TextMeshProUGUI text, int roomIndex)
    {
        roomButtons.Add(button);
        roomTexts.Add(text);
        AddButtonListener(button, () => OnClickRoom(roomIndex));
    }

    private void OnClickRoom(int roomIndex)
    {
        if (roomIndex < 0 || roomIndex >= visibleRooms.Count) {
            return;
        }

        LanRoomInfo room = visibleRooms[roomIndex];
        SetStatus($"\u6b63\u5728\u8fde\u63a5 {room.name} ({room.hostAddress})\u3002");
    }

    private void RefreshRoomList(IReadOnlyList<LanRoomInfo> rooms)
    {
        visibleRooms.Clear();
        if (rooms != null) {
            visibleRooms.AddRange(rooms);
        }

        if (binder.panel_room_list != null) {
            binder.panel_room_list.SetActive(visibleRooms.Count > 0);
        }

        for (int i = 0; i < roomButtons.Count; i++) {
            bool hasRoom = i < visibleRooms.Count;
            if (roomButtons[i] != null) {
                roomButtons[i].gameObject.SetActive(hasRoom);
            }
            if (hasRoom) {
                SetText(roomTexts[i], visibleRooms[i].GetDisplayText());
            }
        }
    }

    private void SetStatus(string status)
    {
        SetText(binder.txt_status, status);
    }

    private void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value;
        }
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }

    private readonly struct LanRoomInfo
    {
        public readonly string name;
        public readonly string hostAddress;
        private readonly int playerCount;
        private readonly int maxPlayerCount;

        public LanRoomInfo(string name, string hostAddress, int playerCount, int maxPlayerCount)
        {
            this.name = name;
            this.hostAddress = hostAddress;
            this.playerCount = playerCount;
            this.maxPlayerCount = maxPlayerCount;
        }

        public string GetDisplayText()
        {
            return $"{name}  {hostAddress}  {playerCount}/{maxPlayerCount}";
        }
    }
}
