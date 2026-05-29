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
        RefreshActionButtons();
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
        RefreshActionButtons();
        TryAutoStartGame();
        TryEnterLanDuel();
    }

    public void OnClickBtnCreateRoom()
    {
        if (Global.Instance.lanRoomService == null) {
            SetStatus(MessageText.Get("lan_room_service_not_ready"));
            return;
        }

        LanRoomSessionState state = Global.Instance.lanRoomService.SessionState;
        if (state.role != LanRoomRole.None) {
            SetStatus(Global.Instance.lanRoomService.LastStatus);
            RefreshActionButtons();
            return;
        }

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
            duelParams.lanHostPlayerFlag,
            duelParams.lanHostPlayerSideCfgId);
        Global.Instance.lanRoomService.CreateRoom(MessageText.Get("lan_room_default_host_name"));
        Global.Instance.lanRoomService.SetLocalReady(true, false);
        RefreshRoomList(null);
        SetStatus(Global.Instance.lanRoomService.LastStatus);
        RefreshActionButtons();
    }

    public void OnClickBtnSearchRoom()
    {
        if (Global.Instance.lanRoomService == null) {
            SetStatus(MessageText.Get("lan_room_service_not_ready"));
            return;
        }

        if (Global.Instance.lanRoomService.IsHosting) {
            SetStatus(Global.Instance.lanRoomService.LastStatus);
            RefreshActionButtons();
            return;
        }

        LanRoomSessionState state = Global.Instance.lanRoomService.SessionState;
        if (state.role != LanRoomRole.None) {
            SetStatus(Global.Instance.lanRoomService.LastStatus);
            RefreshActionButtons();
            return;
        }

        if (!Global.Instance.lanRoomService.StartSearchRooms()) {
            RefreshRoomList(null);
            SetStatus(Global.Instance.lanRoomService.LastStatus);
            RefreshActionButtons();
            return;
        }

        nextSearchRefreshTime = 0f;
        RefreshRoomList(Global.Instance.lanRoomService.GetDiscoveredRooms());
        SetStatus(Global.Instance.lanRoomService.LastStatus);
        RefreshActionButtons();
    }

    public void OnClickBtnClose()
    {
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
            LanRoomSessionState state = Global.Instance.lanRoomService.SessionState;
            if (!state.gameStarted) {
                Global.Instance.lanRoomService.SetLocalReady(true);
            }
        }
        SetStatus(Global.Instance.lanRoomService.LastStatus);
    }

    private void OnLanRoomPeerLeft(OnLanRoomPeerLeft evt)
    {
        RefreshRoomList(null);
        SetStatus(MessageText.Get("lan_room_peer_left"));
        RefreshActionButtons();
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
        LeaveLanSessionIfPopupClosingBeforeDuel();
        ClearRoomItems();
        base.OnClose();
    }

    private void LeaveLanSessionIfPopupClosingBeforeDuel()
    {
        if (hasEnteredLanDuel) {
            return;
        }

        LanRoomService service = Global.Instance.lanRoomService;
        if (service == null) {
            return;
        }

        LanRoomSessionState state = service.SessionState;
        if (service.IsSearching || service.IsHosting || state.role != LanRoomRole.None) {
            service.LeaveCurrentSession(LanRoomLeaveReason.CancelRoom);
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

    private void RefreshActionButtons()
    {
        LanRoomService service = Global.Instance.lanRoomService;
        bool hasService = service != null;
        bool isInSession = false;
        bool isHosting = false;
        bool isSearching = false;

        if (hasService) {
            LanRoomSessionState state = service.SessionState;
            isInSession = state.role != LanRoomRole.None;
            isHosting = service.IsHosting;
            isSearching = service.IsSearching;
        }

        if (binder.btn_create_room != null) {
            binder.btn_create_room.interactable = hasService && !isInSession && !isHosting;
        }
        if (binder.btn_search_room != null) {
            binder.btn_search_room.interactable = hasService && !isInSession && !isHosting && !isSearching;
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
                localPlayerProfile = User.Instance.compUserInfo.BuildProfileData(),
                hostPlayerProfile = service.HostPlayerProfile,
                clientPlayerProfile = service.ClientPlayerProfile,
                isLanDuel = true,
                lanRole = state.role,
                lanHostPlayerFlag = service.LanHostPlayerFlag,
                lanHostPlayerSideCfgId = service.LanHostPlayerSideCfgId,
                isLanRoomHostConfig = true,
            }
        };
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.DUEL_SCENE_TYPE_ID, sceneCreateParams);
    }
}
