using UnityEngine;
using XNClient.Logger;

public class MainMenuPage : UIPageWithBinder<MainMenuPageUI>
{
    public override string pageName => UIPage.GetPageName<MainMenuPage>();
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;
    private bool lastOgsButtonVisible;
    private bool isOgsGameStarting;

    protected override void OnLoaded()
    {
        base.OnLoaded();

        ApplyCurrentLayoutState(true);

        binder.btn_new_game.onClick.AddListener(OnClickBtnNewGame);
        binder.btn_ai_game.onClick.AddListener(OnClickBtnAiGame);
        binder.btn_lan_game.onClick.AddListener(OnClickBtnLanGame);
        binder.btn_ogs_game.onClick.AddListener(OnClickBtnOgsGame);
        binder.btn_exit.onClick.AddListener(OnClickBtnExit);
        binder.btn_user_info.onClick.AddListener(OnClickBtnUserInfo);

        RefreshOgsGameButton(true);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        ApplyCurrentLayoutState(false);
        RefreshOgsGameButton(false);
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ApplyCurrentLayoutState(false);
        RefreshOgsGameButton(false);
    }

    private void ApplyCurrentLayoutState(bool force)
    {
        if (binder.sr_platform == null) {
            return;
        }

        bool isPortrait = UIUtils.IsPortrait(rectTransform);
        if (!force && hasAppliedLayoutState && isPortrait == lastPortraitLayout) {
            return;
        }

        binder.SetSrPlatformState(isPortrait ? MainMenuPageUI.SrPlatformState.Portrait : MainMenuPageUI.SrPlatformState.Landscape, force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;
    }

    public void OnClickBtnNewGame()
    {
        DuelSetupPopup.Open(false);
    }

    public void OnClickBtnAiGame()
    {
        DuelSetupPopup.Open(true);
    }

    public void OnClickBtnLanGame()
    {
        LanRoomPopup.OpenPopup();
    }

    public async void OnClickBtnOgsGame()
    {
        if (isOgsGameStarting) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasWriteSession) {
            RefreshOgsGameButton(true);
            string message = service != null && service.HasSession
                ? "当前 OGS 授权缺少对局权限，请重新登录 OGS。"
                : "请先登录 OGS。";
            ConfirmPopup.ShowTip("OGS 对战", message, null, "确定");
            return;
        }

        isOgsGameStarting = true;
        RefreshOgsGameButton(true);
        int popupRequestId = ConfirmPopup.ShowBlocking("OGS 对战", "正在载入或创建 OGS 9 路人机对局...");

        try {
            OgsBotGameStartResult result = await service.StartOrLoadDefaultBotGameAsync();
            if (!result.success) {
                XNLogger.LogWarn(
                    "OGS bot game start failed.",
                    ("message", result.message),
                    ("gameId", result.gameId.ToString()),
                    ("botId", result.botId.ToString()));
                ConfirmPopup.CloseIfOpen(popupRequestId);
                ConfirmPopup.ShowTip("OGS 对战失败", result.message, null, "确定");
                return;
            }

            ConfirmPopup.CloseIfOpen(popupRequestId);
            EnterOgsDuelScene(result);
        }
        catch (System.Exception ex) {
            XNLogger.LogError("OGS bot game start from main menu failed.", ("err", ex.Message));
            ConfirmPopup.CloseIfOpen(popupRequestId);
            ConfirmPopup.ShowTip("OGS 对战失败", ex.Message, null, "确定");
        }
        finally {
            isOgsGameStarting = false;
            RefreshOgsGameButton(true);
        }
    }

    public void OnClickBtnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        UnityEngine.Application.Quit();
#endif
    }

    public void OnClickBtnUserInfo()
    {
        Global.Instance.uiManager.ShowPage<UserInfoPopup>();
    }

    private void RefreshOgsGameButton(bool force)
    {
        bool visible = Global.Instance.ogsConnectionService != null && Global.Instance.ogsConnectionService.HasWriteSession;
        if (force || visible != lastOgsButtonVisible || binder.btn_ogs_game.gameObject.activeSelf != visible) {
            binder.btn_ogs_game.gameObject.SetActive(visible);
            lastOgsButtonVisible = visible;
        }

        binder.btn_ogs_game.interactable = visible && !isOgsGameStarting;
    }

    private void EnterOgsDuelScene(OgsBotGameStartResult result)
    {
        OgsGameStateSmokeResult gameState = result.gameState;
        int boardSize = gameState != null && gameState.boardWidth > 0 && gameState.boardWidth == gameState.boardHeight
            ? gameState.boardWidth
            : OgsConnectionConfig.DefaultBotGameBoardSize;
        string boardCfgId = $"{boardSize}x{boardSize}";
        SceneCreateParams sceneCreateParams = new SceneCreateParams
        {
            duelSceneCreateParamas = new DuelSceneCreateParamas
            {
                boardCfgId = boardCfgId,
                holdTimeCfgId = "infinite",
                byoyomiCountCfgId = "off",
                byoyomiTimeCfgId = "30s",
            },
            ogsDuelSceneCreateParams = new OgsDuelSceneCreateParams
            {
                gameId = result.gameId,
                boardSize = boardSize,
                botId = result.botId,
                botName = result.botName,
                isBotGame = result.isBotGame,
                challengeId = result.challengeId,
                challengeUuid = result.challengeUuid,
            },
        };
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.OGS_DUEL_SCENE_TYPE_ID, sceneCreateParams);
    }
}
