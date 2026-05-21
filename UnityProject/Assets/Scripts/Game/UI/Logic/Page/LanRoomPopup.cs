using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LanRoomPopup : UIPageWithBinder<LanRoomPopupUI>
{
    private readonly List<Button> roomButtons = new List<Button>();
    private readonly List<TextMeshProUGUI> roomTexts = new List<TextMeshProUGUI>();
    private readonly List<LanRoomInfo> visibleRooms = new List<LanRoomInfo>();
    private float nextSearchRefreshTime;
    private bool hasEnteredLanDuel;

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
        hasEnteredLanDuel = false;
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        if (Global.Instance.lanRoomService == null) {
            return;
        }

        if (Time.unscaledTime < nextSearchRefreshTime) {
            return;
        }

        nextSearchRefreshTime = Time.unscaledTime + 0.5f;
        if (Global.Instance.lanRoomService.IsSearching) {
            RefreshRoomList(Global.Instance.lanRoomService.GetDiscoveredRooms());
        }
        if (Global.Instance.lanRoomService.IsSearching || Global.Instance.lanRoomService.IsHosting) {
            SetStatus(Global.Instance.lanRoomService.LastStatus);
        }
        TryAutoStartGame();
        TryEnterLanDuel();
    }

    public void OnClickBtnCreateRoom()
    {
        if (Global.Instance.lanRoomService == null) {
            SetStatus("\u5c40\u57df\u7f51\u623f\u95f4\u670d\u52a1\u672a\u521d\u59cb\u5316\u3002");
            return;
        }

        Global.Instance.lanRoomService.StopSearchRooms();
        Global.Instance.lanRoomService.CreateRoom("\u6211\u7684\u5c40\u57df\u7f51\u623f\u95f4");
        Global.Instance.lanRoomService.SetLocalReady(true);
        RefreshRoomList(null);
        SetStatus(Global.Instance.lanRoomService.LastStatus);
    }

    public void OnClickBtnSearchRoom()
    {
        if (Global.Instance.lanRoomService == null) {
            SetStatus("\u5c40\u57df\u7f51\u623f\u95f4\u670d\u52a1\u672a\u521d\u59cb\u5316\u3002");
            return;
        }

        Global.Instance.lanRoomService.StartSearchRooms();
        nextSearchRefreshTime = 0f;
        RefreshRoomList(Global.Instance.lanRoomService.GetDiscoveredRooms());
        SetStatus(Global.Instance.lanRoomService.LastStatus);
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
        if (Global.Instance.lanRoomService == null) {
            SetStatus("\u5c40\u57df\u7f51\u623f\u95f4\u670d\u52a1\u672a\u521d\u59cb\u5316\u3002");
            return;
        }

        SetStatus($"\u6b63\u5728\u8fde\u63a5 {room.name} ({room.hostAddress}:{room.tcpPort})\u3002");
        Global.Instance.lanRoomService.StopSearchRooms();
        if (Global.Instance.lanRoomService.ConnectToRoom(room)) {
            Global.Instance.lanRoomService.SetLocalReady(true);
        }
        SetStatus(Global.Instance.lanRoomService.LastStatus);
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

    private void TryAutoStartGame()
    {
        LanRoomService service = Global.Instance.lanRoomService;
        if (service == null) {
            return;
        }

        LanRoomSessionState state = service.SessionState;
        if (state.role == LanRoomRole.Host && state.CanStartGame) {
            service.TryStartGame();
            SetStatus(service.LastStatus);
        }
    }

    private void TryEnterLanDuel()
    {
        if (hasEnteredLanDuel) {
            return;
        }

        LanRoomService service = Global.Instance.lanRoomService;
        if (service == null) {
            return;
        }

        LanRoomSessionState state = service.SessionState;
        if (!state.gameStarted || state.role == LanRoomRole.None) {
            return;
        }

        hasEnteredLanDuel = true;
        SceneCreateParams sceneCreateParams = new SceneCreateParams()
        {
            duelSceneCreateParamas = new DuelSceneCreateParamas()
            {
                boardCfgId = "9x9",
                holdTimeCfgId = "5m",
                byoyomiCountCfgId = "off",
                byoyomiTimeCfgId = "30s",
                isAiDuel = false,
                aiDifficultyCfgId = string.Empty,
                isLanDuel = true,
                lanRole = state.role,
            }
        };
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.DUEL_SCENE_TYPE_ID, sceneCreateParams);
    }
}
