using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XNClient.ChessBoard;

public class DuelPage : UIPageWithBinder<DuelPageUI>
{
    public override string pageName => UIPage.GetPageName<DuelPage>();
    public GameObject aimChessPreview;
    public RectCoordinates aimCoords = new RectCoordinates(-1, -1);
    private PlayerFlag aimChessPreviewPlayerFlag;

    protected override void OnLoaded()
    {
        base.OnLoaded();

        RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);

        BindPrefabHud();
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        SetSettingsPanelVisible(false);
        RefreshDuelHud();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        RefreshDuelHud();

        if (IsSettingsPanelVisible()) {
            aimCoords.SetValue(-1, -1);
            SetAimChessPreviewActive(false);
            return;
        }

        aimCoords.SetValue(-1, -1);
        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_TURN_INPUT) {
            RefreshAimChessPreview(mainScene, compDuel);
        } else {
            SetAimChessPreviewActive(false);
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.Mouse0) && !IsPointerOverUI()) {
            OnMouse0Down();
        }
    }

    protected override void OnClose()
    {
        base.OnClose();
        if (aimChessPreview != null) {
            GameObject.DestroyImmediate(aimChessPreview);
            aimChessPreview = null;
        }
    }

    private void RefreshAimChessPreview(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        Player curPlayer = mainScene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            SetAimChessPreviewActive(false);
            return;
        }

        Ray mouseRay = Global.Instance.uiManager.uiCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(mouseRay.origin, mouseRay.direction, out var hitInfo, 500)) {
            SetAimChessPreviewActive(false);
            return;
        }

        var compChessBoard = mainScene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard == null) {
            SetAimChessPreviewActive(false);
            return;
        }

        Transform gridTransform = compChessBoard.chessBoardGrid.transform;
        Vector3 localHitPoint = gridTransform.InverseTransformPoint(hitInfo.point);
        float cellSideLength = ChessBoardConfig.rectCellSideLength;

        int nearestCellX = Mathf.RoundToInt(localHitPoint.x / cellSideLength - 0.5f);
        int nearestCellZ = Mathf.RoundToInt(localHitPoint.z / cellSideLength - 0.5f);

        int maxCellIndex = Mathf.Max(compChessBoard.chessBoardGrid.gridSize - 1, 0);
        nearestCellX = Mathf.Clamp(nearestCellX, 0, maxCellIndex);
        nearestCellZ = Mathf.Clamp(nearestCellZ, 0, maxCellIndex);

        RectCoordinates nearestCoords = new RectCoordinates(nearestCellX, nearestCellZ);
        int posIndex = compChessBoard.GetPosIndexByCoords(nearestCoords);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            SetAimChessPreviewActive(false);
            return;
        }

        EnsureAimChessPreview((PlayerFlag)curPlayer.playerFlag.value);
        if (aimChessPreview == null) {
            return;
        }

        Vector3 nearestCellCenterLocalPos = new Vector3(
            (nearestCellX + 0.5f) * cellSideLength,
            0f,
            (nearestCellZ + 0.5f) * cellSideLength
        );
        aimChessPreview.transform.position = gridTransform.TransformPoint(nearestCellCenterLocalPos);
        aimCoords.SetValue(nearestCoords.x, nearestCoords.z);
        SetAimChessPreviewActive(true);
    }

    private void EnsureAimChessPreview(PlayerFlag playerFlag)
    {
        if (aimChessPreview != null && aimChessPreviewPlayerFlag == playerFlag) {
            return;
        }

        if (aimChessPreview != null) {
            GameObject.DestroyImmediate(aimChessPreview);
            aimChessPreview = null;
        }

        string gamePrefabTypeId = DuelUtils.GetGamePrefabTypeIdWithPlayerFlag(playerFlag);
        var gamePrefabCfg = GamePrefabDataType.GetConfigData(gamePrefabTypeId);
        if (gamePrefabCfg == null) {
            return;
        }

        aimChessPreview = Global.Instance.resourceManager.LoadGamePrefab(gamePrefabCfg.resPath);
        if (aimChessPreview == null) {
            return;
        }

        aimChessPreviewPlayerFlag = playerFlag;
        SetAimChessPreviewActive(false);
        foreach (var collider in aimChessPreview.GetComponentsInChildren<Collider>()) {
            collider.enabled = false;
        }
    }

    private void SetAimChessPreviewActive(bool isActive)
    {
        if (aimChessPreview != null) {
            aimChessPreview.SetActive(isActive);
        }
    }

    public void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        RefreshDuelHud();
    }

    public void RefreshDuelHud()
    {
        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return;
        }

        Player blackPlayer = mainScene.GetEntity<Player>(compDuel.player1Guid.value);
        Player whitePlayer = mainScene.GetEntity<Player>(compDuel.player2Guid.value);
        string curTurnPlayerGuid = compDuel.duelFSM.isActivated ? compDuel.curTurnPlayerGuid.value : string.Empty;

        RefreshPlayerInfoPanel(
            binder.txt_black_title,
            binder.txt_black_hold_time,
            binder.txt_black_byoyomi_count,
            binder.txt_black_byoyomi_time,
            blackPlayer,
            curTurnPlayerGuid,
            "黑方"
        );
        RefreshPlayerInfoPanel(
            binder.txt_white_title,
            binder.txt_white_hold_time,
            binder.txt_white_byoyomi_count,
            binder.txt_white_byoyomi_time,
            whitePlayer,
            curTurnPlayerGuid,
            "白方"
        );
    }

    public void OnMouse0Down()
    {
        if (IsSettingsPanelVisible()) {
            return;
        }

        var mainScene = Global.Instance.sceneManager.mainScene;
        var compDuel = mainScene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_TURN_INPUT) {
            EmitSystemEvent(new OnAddChessToBoard(aimCoords.Clone()));
        }
    }

    public void OnClickBtnSave()
    {
        EmitSystemEvent(new OnSaveDuelScene());
    }

    public void OnClickBtnExit()
    {
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
    }

    private void BindPrefabHud()
    {
        AddButtonListener(binder.btn_duel_settings, OpenSettingsPanel);
        AddButtonListener(binder.btn_settings_save, OnClickBtnSave);
        AddButtonListener(binder.btn_settings_exit, OnClickBtnExit);
        AddButtonListener(binder.btn_settings_close, CloseSettingsPanel);
    }

    private void RefreshPlayerInfoPanel(
        TextMeshProUGUI titleText,
        TextMeshProUGUI holdText,
        TextMeshProUGUI byoyomiCountText,
        TextMeshProUGUI byoyomiTimeText,
        Player player,
        string curTurnPlayerGuid,
        string title
    )
    {
        bool isCurTurnPlayer = player != null && player.guid == curTurnPlayerGuid;
        SetText(titleText, isCurTurnPlayer ? $"{title}  行棋中" : title);

        var compDuelInfo = player?.GetComponent<ComponentDuelInfo>();
        if (compDuelInfo == null) {
            SetText(holdText, "持有时间: --");
            SetText(byoyomiCountText, "读秒次数: --");
            SetText(byoyomiTimeText, "读秒时间: --");
            return;
        }

        SetText(holdText, $"持有时间: {FormatSeconds(compDuelInfo.holdLeftSeconds.value, compDuelInfo.isInfiniteTime.value)}");
        SetText(byoyomiCountText, $"读秒次数: {compDuelInfo.byoyomiLeftCount.value}");
        SetText(byoyomiTimeText, $"读秒时间: {FormatSeconds(compDuelInfo.byoyomiLeftSeconds.value, false)}");
    }

    private string FormatSeconds(int seconds, bool isInfinite)
    {
        if (isInfinite || seconds < 0) {
            return "--";
        }

        int safeSeconds = Mathf.Max(seconds, 0);
        int minutes = safeSeconds / 60;
        int remainSeconds = safeSeconds % 60;
        return $"{minutes:00}:{remainSeconds:00}";
    }

    private void OpenSettingsPanel()
    {
        SetSettingsPanelVisible(true);
    }

    private void CloseSettingsPanel()
    {
        SetSettingsPanelVisible(false);
    }

    private void SetSettingsPanelVisible(bool isVisible)
    {
        if (binder.panel_duel_settings != null) {
            binder.panel_duel_settings.SetActive(isVisible);
        }
    }

    private bool IsSettingsPanelVisible()
    {
        return binder.panel_duel_settings != null && binder.panel_duel_settings.activeSelf;
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }

    private void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value;
        }
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
