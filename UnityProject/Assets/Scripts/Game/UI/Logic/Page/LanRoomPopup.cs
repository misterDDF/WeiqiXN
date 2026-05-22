using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LanRoomPopup : UIPageWithBinder<LanRoomPopupUI>
{
    private readonly List<LanRoomInfo> visibleRooms = new List<LanRoomInfo>();
    private readonly List<LanRoomItemWidget> roomItems = new List<LanRoomItemWidget>();
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
        RegisterSystemEvent<OnLanRoomPeerLeft>(OnLanRoomPeerLeft);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        SetStatus(MessageText.Get("lan_room_choose_action"));
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
        DuelSetupPopup.OpenForLanRoom(CreateRoomWithConfig);
    }

    private void CreateRoomWithConfig(DuelSceneCreateParamas duelParams)
    {
        if (Global.Instance.lanRoomService == null) {
            SetStatus(MessageText.Get("lan_room_service_not_ready"));
            return;
        }

        Global.Instance.lanRoomService.StopSearchRooms();
        Global.Instance.lanRoomService.SetStartConfig(
            duelParams.boardCfgId,
            duelParams.holdTimeCfgId,
            duelParams.byoyomiCountCfgId,
            duelParams.byoyomiTimeCfgId,
            duelParams.handicapCfgId,
            duelParams.lanHostPlayerFlag);
        Global.Instance.lanRoomService.CreateRoom(MessageText.Get("lan_room_default_host_name"));
        Global.Instance.lanRoomService.SetLocalReady(true);
        RefreshRoomList(null);
        SetStatus(Global.Instance.lanRoomService.LastStatus);
    }

    public void OnClickBtnSearchRoom()
    {
        if (Global.Instance.lanRoomService == null) {
            SetStatus(MessageText.Get("lan_room_service_not_ready"));
            return;
        }

        Global.Instance.lanRoomService.StartSearchRooms();
        nextSearchRefreshTime = 0f;
        RefreshRoomList(Global.Instance.lanRoomService.GetDiscoveredRooms());
        SetStatus(Global.Instance.lanRoomService.LastStatus);
    }

    public void OnClickBtnClose()
    {
        if (!hasEnteredLanDuel) {
            Global.Instance.lanRoomService?.LeaveCurrentSession(LanRoomLeaveReason.CancelRoom);
        }
        ClosePage();
    }

    private void OnClickRoom(LanRoomInfo room)
    {
        if (Global.Instance.lanRoomService == null) {
            SetStatus(MessageText.Get("lan_room_service_not_ready"));
            return;
        }

        SetStatus(MessageText.Format("lan_room_connecting", room.name, room.hostAddress, room.tcpPort));
        Global.Instance.lanRoomService.StopSearchRooms();
        if (Global.Instance.lanRoomService.ConnectToRoom(room)) {
            Global.Instance.lanRoomService.SetLocalReady(true);
        }
        SetStatus(Global.Instance.lanRoomService.LastStatus);
    }

    private void OnLanRoomPeerLeft(OnLanRoomPeerLeft evt)
    {
        RefreshRoomList(null);
        SetStatus(MessageText.Get("lan_room_peer_left"));
    }

    private void RefreshRoomList(IReadOnlyList<LanRoomInfo> rooms)
    {
        ClearRoomItems();
        visibleRooms.Clear();
        if (rooms != null) {
            visibleRooms.AddRange(rooms);
        }

        if (binder.panel_room_list != null) {
            binder.panel_room_list.SetActive(visibleRooms.Count > 0);
        }

        if (binder.content_room_list == null) {
            return;
        }

        foreach (LanRoomInfo room in visibleRooms) {
            LanRoomItemWidget item = CreateRoomItemWidget();
            if (item != null) {
                item.SetData(room, OnClickRoom);
                roomItems.Add(item);
            }
        }
    }

    private LanRoomItemWidget CreateRoomItemWidget()
    {
        GameObject roomItemGO = Global.Instance.resourceManager.LoadGamePrefab(
            UIUtils.GetWidgetPrefabPath(UIWidget.GetWidgetName<LanRoomItemWidget>()));
        if (roomItemGO == null) {
            return null;
        }

        LanRoomItemWidget item = UIWidget.CreateWidgetInstance<LanRoomItemWidget>(this);
        item.OnUnityResourceLoaded(roomItemGO);
        item.transform.SetParent(binder.content_room_list, false);
        return item;
    }

    private void ClearRoomItems()
    {
        foreach (LanRoomItemWidget item in roomItems) {
            if (item != null && item.isLoaded && item.gameObject != null) {
                item.CloseWidget();
                GameObject.Destroy(item.gameObject);
            }
        }

        roomItems.Clear();
    }

    protected override void OnClose()
    {
        ClearRoomItems();
        base.OnClose();
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
                boardCfgId = service.LanBoardCfgId,
                holdTimeCfgId = service.LanHoldTimeCfgId,
                byoyomiCountCfgId = service.LanByoyomiCountCfgId,
                byoyomiTimeCfgId = service.LanByoyomiTimeCfgId,
                handicapCfgId = service.LanHandicapCfgId,
                isAiDuel = false,
                aiDifficultyCfgId = string.Empty,
                localPlayerFlag = 0,
                isLanDuel = true,
                lanRole = state.role,
                lanHostPlayerFlag = service.LanHostPlayerFlag,
                isLanRoomHostConfig = true,
            }
        };
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.DUEL_SCENE_TYPE_ID, sceneCreateParams);
    }
}
