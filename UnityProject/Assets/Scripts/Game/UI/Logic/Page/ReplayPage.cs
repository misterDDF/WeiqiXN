public class ReplayPage : UIPageWithBinder<ReplayPageUI>
{
    public override string pageName => UIPage.GetPageName<ReplayPage>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        binder.btn_close.onClick.AddListener(OnClickClose);
        binder.btn_first.onClick.AddListener(OnClickFirst);
        binder.btn_prev.onClick.AddListener(OnClickPrev);
        binder.btn_next.onClick.AddListener(OnClickNext);
        binder.btn_last.onClick.AddListener(OnClickLast);
        binder.btn_try_mode.onClick.AddListener(OnClickTryMode);
    }

    protected override void OnClose()
    {
        if (binder != null) {
            binder.btn_close.onClick.RemoveListener(OnClickClose);
            binder.btn_first.onClick.RemoveListener(OnClickFirst);
            binder.btn_prev.onClick.RemoveListener(OnClickPrev);
            binder.btn_next.onClick.RemoveListener(OnClickNext);
            binder.btn_last.onClick.RemoveListener(OnClickLast);
            binder.btn_try_mode.onClick.RemoveListener(OnClickTryMode);
        }

        base.OnClose();
    }

    protected override void OnOpen()
    {
        base.OnOpen();
        RefreshControls();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        RefreshControls();
    }

    private void RefreshControls()
    {
        ReplayScene replayScene = Global.Instance.sceneManager.mainScene as ReplayScene;
        bool canBrowse = replayScene != null && replayScene.IsReplayLoaded && replayScene.ReplayMoveCount > 0;

        if (binder.txt_board != null && binder.txt_board.transform.parent != null) {
            binder.txt_board.transform.parent.gameObject.SetActive(false);
        }
        binder.txt_title.text = replayScene != null ? replayScene.configData.id : "Replay";
        binder.txt_summary.text = replayScene != null ? replayScene.BuildSummaryText() : "未加载复盘场景";
        binder.txt_status.text = replayScene != null ? replayScene.ReplayStatus : string.Empty;
        binder.txt_move_cursor.text = replayScene != null ? replayScene.BuildCursorText() : "0 / 0";
        binder.txt_move_detail.text = replayScene != null ? replayScene.BuildMoveDetailText() : "未加载复盘";
        binder.txt_analysis_placeholder.text = replayScene != null ? replayScene.BuildActionHint() : "试下、图表和 AI 推荐将在后续复盘控制层接入。";
        binder.btn_first.interactable = canBrowse;
        binder.btn_prev.interactable = canBrowse;
        binder.btn_next.interactable = canBrowse;
        binder.btn_last.interactable = canBrowse;
        binder.btn_try_mode.interactable = false;
    }

    private void OnClickClose()
    {
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
    }

    private void OnClickFirst()
    {
        (Global.Instance.sceneManager.mainScene as ReplayScene)?.GoFirst();
    }

    private void OnClickPrev()
    {
        (Global.Instance.sceneManager.mainScene as ReplayScene)?.GoPrev();
    }

    private void OnClickNext()
    {
        (Global.Instance.sceneManager.mainScene as ReplayScene)?.GoNext();
    }

    private void OnClickLast()
    {
        (Global.Instance.sceneManager.mainScene as ReplayScene)?.GoLast();
    }

    private void OnClickTryMode()
    {
        binder.txt_status.text = "试下模式将在后续阶段接入";
    }
}
