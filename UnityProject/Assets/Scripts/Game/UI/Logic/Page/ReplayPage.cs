using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XNClient.ChessBoard;

public class ReplayPage : UIPageWithBinder<ReplayPageUI>
{
    private const int LargeStepMoveCount = 5;

    private DuelPageBoardInputController boardInput;
    private bool isScrubbing;
    private int scrubTargetMoveIndex;

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
        BindScrubberEvents();
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
            UnbindScrubberEvents();
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
        bool isTryMode = replaySystem != null && replaySystem.IsReplayLoaded && replaySystem.IsTryMode;
        bool canAiAnalysis = replaySystem != null && replaySystem.IsReplayLoaded && !replaySystem.IsAiAnalyzing &&
            (replaySystem.IsAiAnalysisEnabled || replaySystem.HasAiAnalysisRender);

        binder.txt_title.text = replayScene != null ? replayScene.configData.id : "Replay";
        binder.txt_summary.text = replaySystem != null ? replaySystem.BuildSummaryText() : "未加载复盘场景";
        binder.txt_status.text = replaySystem != null ? replaySystem.ReplayStatus : string.Empty;
        binder.txt_move_cursor.text = replaySystem != null ? replaySystem.BuildCursorText() : "0 / 0";
        binder.txt_move_detail.text = replaySystem != null ? replaySystem.BuildMoveDetailText() : "未加载复盘";
        RefreshChartProgress(replaySystem);
        RefreshScrubPreview(replaySystem);
        RefreshChart(replaySystem);
        binder.btn_first.interactable = canBrowse;
        binder.btn_prev.interactable = canBrowse;
        binder.btn_next.interactable = canBrowse;
        binder.btn_last.interactable = canBrowse;
        if (binder.btn_try_mode != null) {
            binder.btn_try_mode.gameObject.SetActive(isTryMode);
            binder.btn_try_mode.interactable = isTryMode;
        }
        if (binder.btn_ai_analysis != null) {
            binder.btn_ai_analysis.interactable = canAiAnalysis;
        }
        SetButtonText(binder.btn_close, "退出");
        SetTryModeButtonText("取消试下");
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
        GetReplaySystem()?.GoRelative(-LargeStepMoveCount);
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
        GetReplaySystem()?.GoRelative(LargeStepMoveCount);
    }

    private void OnClickTryMode()
    {
        GetReplaySystem()?.ExitTryMode();
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

    private void BindScrubberEvents()
    {
        if (binder.img_move_scrubber_hit == null) {
            return;
        }

        UIEventTrigger trigger = binder.img_move_scrubber_hit.GetOrAddComponent<UIEventTrigger>();
        trigger.onPointerDownHandler = OnScrubPointerDown;
        trigger.onBeginDragHandler = OnScrubDrag;
        trigger.onDragHandler = OnScrubDrag;
        trigger.onEndDragHandler = OnScrubEndDrag;
        trigger.onPointerUpHandler = OnScrubPointerUp;
    }

    private void UnbindScrubberEvents()
    {
        if (binder.img_move_scrubber_hit == null) {
            return;
        }

        UIEventTrigger trigger = binder.img_move_scrubber_hit.GetComponent<UIEventTrigger>();
        if (trigger == null) {
            return;
        }

        trigger.onPointerDownHandler = null;
        trigger.onBeginDragHandler = null;
        trigger.onDragHandler = null;
        trigger.onEndDragHandler = null;
        trigger.onPointerUpHandler = null;
    }

    private void OnScrubPointerDown(PointerEventData eventData)
    {
        isScrubbing = true;
        UpdateScrubTarget(eventData);
    }

    private void OnScrubDrag(PointerEventData eventData)
    {
        isScrubbing = true;
        UpdateScrubTarget(eventData);
    }

    private void OnScrubEndDrag(PointerEventData eventData)
    {
        UpdateScrubTarget(eventData);
        ApplyScrubTarget();
    }

    private void OnScrubPointerUp(PointerEventData eventData)
    {
        if (!isScrubbing) {
            return;
        }

        UpdateScrubTarget(eventData);
        ApplyScrubTarget();
    }

    private void UpdateScrubTarget(PointerEventData eventData)
    {
        ReplaySystem replaySystem = GetReplaySystem();
        if (replaySystem == null || !replaySystem.IsReplayLoaded || binder.img_move_scrubber_hit == null) {
            return;
        }

        RectTransform rectTransform = binder.img_move_scrubber_hit.rectTransform;
        Camera eventCamera = eventData != null ? eventData.pressEventCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventCamera, out Vector2 localPoint)) {
            return;
        }

        Rect rect = rectTransform.rect;
        float normalized = rect.width <= 0f ? 0f : Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        scrubTargetMoveIndex = Mathf.RoundToInt(Mathf.Clamp01(normalized) * replaySystem.ReplayMoveCount);
        RefreshScrubPreview(replaySystem);
        RefreshChartCursor(replaySystem, scrubTargetMoveIndex);
    }

    private void ApplyScrubTarget()
    {
        ReplaySystem replaySystem = GetReplaySystem();
        if (replaySystem != null && replaySystem.IsReplayLoaded) {
            replaySystem.GoToReplayMove(scrubTargetMoveIndex);
        }

        isScrubbing = false;
    }

    private void RefreshScrubPreview(ReplaySystem replaySystem)
    {
        if (binder.txt_scrub_preview == null) {
            return;
        }

        if (replaySystem == null || !replaySystem.IsReplayLoaded) {
            binder.txt_scrub_preview.text = string.Empty;
            return;
        }

        int targetMoveIndex = isScrubbing ? scrubTargetMoveIndex : replaySystem.ReplayCursorMoveIndex;
        string previewText = isScrubbing
            ? replaySystem.BuildScrubPreviewText(targetMoveIndex)
            : replaySystem.BuildChartSummaryText();
        binder.txt_scrub_preview.text = previewText;
    }

    private void RefreshChartProgress(ReplaySystem replaySystem)
    {
        if (binder.txt_analysis_placeholder == null) {
            return;
        }

        string progressText = replaySystem != null ? replaySystem.BuildChartProgressText() : string.Empty;
        binder.txt_analysis_placeholder.text = progressText;
        binder.txt_analysis_placeholder.gameObject.SetActive(!string.IsNullOrEmpty(progressText));
    }

    private void RefreshChart(ReplaySystem replaySystem)
    {
        if (binder.chart_analysis != null) {
            binder.chart_analysis.SetData(replaySystem?.ChartPoints, replaySystem != null ? replaySystem.ReplayMoveCount : 0);
        }

        int cursorMoveIndex = replaySystem != null ? replaySystem.ReplayCursorMoveIndex : 0;
        RefreshChartCursor(replaySystem, isScrubbing ? scrubTargetMoveIndex : cursorMoveIndex);
    }

    private void RefreshChartCursor(ReplaySystem replaySystem, int targetMoveIndex)
    {
        if (binder.img_chart_cursor == null || binder.img_move_scrubber_hit == null || replaySystem == null || !replaySystem.IsReplayLoaded) {
            if (binder.img_chart_cursor != null) {
                binder.img_chart_cursor.enabled = false;
            }
            return;
        }

        RectTransform hitRect = binder.img_move_scrubber_hit.rectTransform;
        RectTransform cursorRect = binder.img_chart_cursor.rectTransform;
        float normalized = replaySystem.ReplayMoveCount <= 0
            ? 0f
            : Mathf.Clamp01((float)Mathf.Clamp(targetMoveIndex, 0, replaySystem.ReplayMoveCount) / replaySystem.ReplayMoveCount);
        float localX = Mathf.Lerp(hitRect.rect.xMin, hitRect.rect.xMax, normalized);
        cursorRect.anchoredPosition = new Vector2(localX, cursorRect.anchoredPosition.y);
        binder.img_chart_cursor.enabled = true;
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
