using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XNClient.ChessBoard;

public class ReplayPage : UIPageWithBinder<ReplayPageUI>
{
    private const float HudPanelAlpha = 0.72f;
    private const float RootPanelAlpha = 0f;
    private bool hudLayoutApplied;
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

        ApplyHudLayout();
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

    private void ApplyHudLayout()
    {
        if (hudLayoutApplied || binder == null) {
            return;
        }

        RectTransform rootPanel = binder.txt_title != null ? binder.txt_title.transform.parent as RectTransform : null;
        if (rootPanel == null) {
            return;
        }

        SetRect(rootPanel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        SetPanelImage(rootPanel, new Color(0f, 0f, 0f, RootPanelAlpha), false);

        if (binder.txt_board != null && binder.txt_board.transform.parent != null) {
            binder.txt_board.transform.parent.gameObject.SetActive(false);
        }

        RectTransform sidePanel = binder.txt_move_detail != null ? binder.txt_move_detail.transform.parent as RectTransform : null;
        RectTransform controlsPanel = binder.txt_move_cursor != null ? binder.txt_move_cursor.transform.parent as RectTransform : null;

        if (sidePanel != null) {
            SetRect(
                sidePanel,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-24f, -24f),
                new Vector2(320f, 146f));
            SetPanelImage(sidePanel, new Color(0.08f, 0.08f, 0.08f, HudPanelAlpha), false);
        }

        if (controlsPanel != null) {
            SetRect(
                controlsPanel,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 32f),
                new Vector2(560f, 64f));
            SetPanelImage(controlsPanel, new Color(0.08f, 0.08f, 0.08f, HudPanelAlpha), true);
        }

        ConfigureTopHud(rootPanel, controlsPanel);
        ConfigureHudText();
        hudLayoutApplied = true;
    }

    private void ConfigureTopHud(RectTransform rootPanel, RectTransform controlsPanel)
    {
        RectTransform titleRect = binder.txt_title != null ? binder.txt_title.rectTransform : null;
        RectTransform summaryRect = binder.txt_summary != null ? binder.txt_summary.rectTransform : null;
        RectTransform statusRect = binder.txt_status != null ? binder.txt_status.rectTransform : null;
        RectTransform closeRect = binder.btn_close != null ? binder.btn_close.transform as RectTransform : null;

        if (titleRect != null) {
            SetRect(titleRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -28f), new Vector2(220f, 36f));
        }
        if (summaryRect != null) {
            SetRect(summaryRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -62f), new Vector2(260f, 28f));
        }
        if (statusRect != null) {
            SetRect(statusRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -92f), new Vector2(280f, 28f));
        }
        if (closeRect != null) {
            SetRect(closeRect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -178f), new Vector2(96f, 38f));
        }

        if (controlsPanel != null) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(controlsPanel);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(rootPanel);
    }

    private void ConfigureHudText()
    {
        SetTextRaycastTarget(binder.txt_title, false);
        SetTextRaycastTarget(binder.txt_summary, false);
        SetTextRaycastTarget(binder.txt_status, false);
        SetTextRaycastTarget(binder.txt_move_cursor, false);
        SetTextRaycastTarget(binder.txt_move_detail, false);
        SetTextRaycastTarget(binder.txt_analysis_placeholder, false);

        SetTextSize(binder.txt_title, 24f);
        SetTextSize(binder.txt_summary, 16f);
        SetTextSize(binder.txt_status, 16f);
        SetTextSize(binder.txt_move_cursor, 18f);
        SetTextSize(binder.txt_move_detail, 18f);
        SetTextSize(binder.txt_analysis_placeholder, 15f);
    }

    private void SetPanelImage(RectTransform rectTransform, Color color, bool raycastTarget)
    {
        Image image = rectTransform != null ? rectTransform.GetComponent<Image>() : null;
        if (image == null) {
            return;
        }

        image.color = color;
        image.raycastTarget = raycastTarget;
    }

    private void SetTextRaycastTarget(TextMeshProUGUI text, bool raycastTarget)
    {
        if (text != null) {
            text.raycastTarget = raycastTarget;
        }
    }

    private void SetTextSize(TextMeshProUGUI text, float fontSize)
    {
        if (text != null) {
            text.fontSize = fontSize;
        }
    }

    private void SetRect(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        if (rectTransform == null) {
            return;
        }

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;
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
