using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XNClient.ChessBoard;

public class ReplayPage : UIPageWithBinder<ReplayPageUI>
{
    private DuelPageBoardInputController boardInput;

    public override string pageName => UIPage.GetPageName<ReplayPage>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        boardInput = new DuelPageBoardInputController();
        binder.btn_close.onClick.AddListener(OnClickClose);
        binder.btn_first.onClick.AddListener(OnClickFirst);
        binder.btn_prev.onClick.AddListener(OnClickPrev);
        binder.btn_next.onClick.AddListener(OnClickNext);
        binder.btn_last.onClick.AddListener(OnClickLast);
        binder.btn_try_mode.onClick.AddListener(OnClickTryMode);
        if (binder.btn_ai_analysis != null) {
            binder.btn_ai_analysis.onClick.AddListener(OnClickAiAnalysis);
        }
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
            if (binder.btn_ai_analysis != null) {
                binder.btn_ai_analysis.onClick.RemoveListener(OnClickAiAnalysis);
            }
        }

        boardInput?.Dispose();
        boardInput = null;
        base.OnClose();
    }

    protected override void OnOpen()
    {
        base.OnOpen();
        if (boardInput == null) {
            boardInput = new DuelPageBoardInputController();
        }

        RefreshControls();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        RefreshControls();
        RefreshTryModeInput();

        if (Input.GetKeyDown(KeyCode.Mouse0)) {
            OnMouse0Down();
        }
        if (Input.GetKeyDown(KeyCode.Mouse1)) {
            OnClickPrev();
        }
    }

    private void RefreshControls()
    {
        ReplaySystem replaySystem = GetReplaySystem();
        ReplayScene replayScene = Global.Instance.sceneManager.mainScene as ReplayScene;
        bool canBrowse = replaySystem != null && replaySystem.IsReplayLoaded &&
            (replaySystem.IsTryMode ? replaySystem.TryMoveCount > 0 : replaySystem.ReplayMoveCount > 0);
        bool canTryMode = replaySystem != null && replaySystem.IsReplayLoaded;
        bool canAiAnalysis = replaySystem != null && replaySystem.IsReplayLoaded && !replaySystem.IsAiAnalyzing &&
            (replaySystem.IsAiAnalysisEnabled || replaySystem.HasAiAnalysisRender);

        binder.txt_title.text = replayScene != null ? replayScene.configData.id : "Replay";
        binder.txt_summary.text = replaySystem != null ? replaySystem.BuildSummaryText() : "未加载复盘场景";
        binder.txt_status.text = replaySystem != null ? replaySystem.ReplayStatus : string.Empty;
        binder.txt_move_cursor.text = replaySystem != null ? replaySystem.BuildCursorText() : "0 / 0";
        binder.txt_move_detail.text = replaySystem != null ? replaySystem.BuildMoveDetailText() : "未加载复盘";
        binder.txt_analysis_placeholder.text = replaySystem != null ? replaySystem.BuildActionHint() : "试下、图表和 AI 推荐将在后续复盘控制层接入。";
        binder.btn_first.interactable = canBrowse;
        binder.btn_prev.interactable = canBrowse;
        binder.btn_next.interactable = canBrowse;
        binder.btn_last.interactable = canBrowse;
        binder.btn_try_mode.interactable = canTryMode;
        if (binder.btn_ai_analysis != null) {
            binder.btn_ai_analysis.interactable = canAiAnalysis;
        }
        SetButtonText(binder.btn_close, "退出");
        SetTryModeButtonText(replaySystem != null && replaySystem.IsTryMode ? "退出试下" : "试下");
        SetButtonText(binder.btn_ai_analysis, GetAiAnalysisButtonText(replaySystem));
    }

    private void RefreshTryModeInput()
    {
        ReplaySystem replaySystem = GetReplaySystem();
        SceneBase mainScene = Global.Instance.sceneManager.mainScene;
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        DuelInputAuthorityState inputState = replaySystem != null && replaySystem.IsReplayLoaded
            ? new DuelInputAuthorityState(replaySystem.CurrentTryPlayerFlag)
            : default;
        boardInput?.Refresh(mainScene, compDuel, inputState, false);
    }

    private void OnClickClose()
    {
        ClosePage();
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
    }

    private void OnClickFirst()
    {
        GetReplaySystem()?.GoFirst();
    }

    private void OnClickPrev()
    {
        GetReplaySystem()?.GoPrev();
    }

    private void OnClickNext()
    {
        GetReplaySystem()?.GoNext();
    }

    private void OnClickLast()
    {
        GetReplaySystem()?.GoLast();
    }

    private void OnClickTryMode()
    {
        GetReplaySystem()?.ToggleTryMode();
    }

    private void OnClickAiAnalysis()
    {
        ReplaySystem replaySystem = GetReplaySystem();
        if (replaySystem == null) {
            return;
        }

        if (replaySystem.HasAiAnalysisRender) {
            replaySystem.ClearAiAnalysisRender();
            return;
        }

        replaySystem.RequestAiAnalysis();
    }

    private void OnMouse0Down()
    {
        ReplaySystem replaySystem = GetReplaySystem();
        if (replaySystem == null || !replaySystem.IsReplayLoaded) {
            return;
        }

        DuelInputAuthorityState inputState = new DuelInputAuthorityState(replaySystem.CurrentTryPlayerFlag);
        if (boardInput == null || !boardInput.TryGetMoveCoords(inputState, out RectCoordinates coords)) {
            return;
        }

        replaySystem.TryApplyBoardMove(coords);
    }

    private ReplaySystem GetReplaySystem()
    {
        return Global.Instance.sceneManager.mainScene?.GetSystem<ReplaySystem>();
    }

    private void SetTryModeButtonText(string text)
    {
        SetButtonText(binder.btn_try_mode, text);
    }

    private string GetAiAnalysisButtonText(ReplaySystem replaySystem)
    {
        if (replaySystem != null && replaySystem.IsAiAnalyzing) {
            return "分析中";
        }

        if (replaySystem != null && replaySystem.HasAiAnalysisRender) {
            return "关闭ai推荐";
        }

        return "AI分析";
    }

    private void SetButtonText(Button button, string text)
    {
        if (button == null) {
            return;
        }

        TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null) {
            buttonText.text = text;
        }
    }
}
