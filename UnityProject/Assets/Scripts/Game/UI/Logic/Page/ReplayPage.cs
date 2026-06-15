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
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;
    private int scrubTargetMoveIndex;

    public override string pageName => UIPage.GetPageName<ReplayPage>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        boardInput = new DuelPageBoardInputController();
        ApplyCurrentLayoutState(true);

        binder.btn_close.onClick.AddListener(OnClickClose);
        binder.btn_first.onClick.AddListener(OnClickFirst);
        binder.btn_prev.onClick.AddListener(OnClickPrev);
        binder.btn_next.onClick.AddListener(OnClickNext);
        binder.btn_last.onClick.AddListener(OnClickLast);
        binder.btn_try_mode.onClick.AddListener(OnClickTryMode);
        if (binder.toggle_move_color_auto != null) {
            binder.toggle_move_color_auto.onValueChanged.AddListener(OnToggleMoveColorAuto);
        }
        if (binder.toggle_move_color_black != null) {
            binder.toggle_move_color_black.onValueChanged.AddListener(OnToggleMoveColorBlack);
        }
        if (binder.toggle_move_color_white != null) {
            binder.toggle_move_color_white.onValueChanged.AddListener(OnToggleMoveColorWhite);
        }
        if (binder.btn_ai_analysis != null) {
            binder.btn_ai_analysis.onClick.AddListener(OnClickAiAnalysis);
        }
        if (binder.btn_ownership != null) {
            binder.btn_ownership.onClick.AddListener(OnClickOwnership);
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
            if (binder.toggle_move_color_auto != null) {
                binder.toggle_move_color_auto.onValueChanged.RemoveListener(OnToggleMoveColorAuto);
            }
            if (binder.toggle_move_color_black != null) {
                binder.toggle_move_color_black.onValueChanged.RemoveListener(OnToggleMoveColorBlack);
            }
            if (binder.toggle_move_color_white != null) {
                binder.toggle_move_color_white.onValueChanged.RemoveListener(OnToggleMoveColorWhite);
            }
            if (binder.btn_ai_analysis != null) {
                binder.btn_ai_analysis.onClick.RemoveListener(OnClickAiAnalysis);
            }
            if (binder.btn_ownership != null) {
                binder.btn_ownership.onClick.RemoveListener(OnClickOwnership);
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

        ApplyCurrentLayoutState(false);
        RefreshControls();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        ApplyCurrentLayoutState(false);
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
        bool hasAiAnalysisRender = replaySystem != null && replaySystem.HasAiAnalysisRender;
        bool canAiAnalysis = replaySystem != null && replaySystem.IsReplayLoaded &&
            (hasAiAnalysisRender || (!replaySystem.IsAiAnalyzing && replaySystem.IsAiAnalysisEnabled));
        bool canOwnership = replaySystem != null && replaySystem.IsReplayLoaded && !replaySystem.IsOwnershipAnalyzing;

        binder.txt_title.text = replayScene != null ? replayScene.configData.id : "Replay";
        binder.txt_summary.text = replaySystem != null ? replaySystem.BuildSummaryText() : "未加载复盘场景";
        binder.txt_status.text = replaySystem != null ? replaySystem.ReplayStatus : string.Empty;
        binder.txt_move_cursor.text = replaySystem != null ? replaySystem.BuildCursorText() : "0 / 0";
        binder.txt_move_detail.text = replaySystem != null ? replaySystem.BuildMoveDetailText() : "未加载复盘";
        RefreshChartProgress(replaySystem);
        RefreshScrubPreview(replaySystem);
        RefreshChart(replaySystem);
        RefreshOwnershipResult(replaySystem);
        binder.btn_first.interactable = canBrowse;
        binder.btn_prev.interactable = canBrowse;
        binder.btn_next.interactable = canBrowse;
        binder.btn_last.interactable = canBrowse;
        if (binder.panel_try_mode != null) {
            binder.panel_try_mode.SetActive(isTryMode);
        } else if (binder.btn_try_mode != null) {
            binder.btn_try_mode.gameObject.SetActive(isTryMode);
        }
        if (binder.btn_try_mode != null) {
            binder.btn_try_mode.interactable = isTryMode;
        }
        if (binder.panel_move_color != null) {
            binder.panel_move_color.SetActive(isTryMode);
        }
        RefreshMoveColorButtons(replaySystem, isTryMode);
        if (binder.btn_ai_analysis != null) {
            binder.btn_ai_analysis.interactable = canAiAnalysis;
        }
        if (binder.btn_ownership != null) {
            binder.btn_ownership.interactable = canOwnership;
        }
        SetButtonText(binder.btn_close, "退出");
        SetTryModeButtonText("取消试下");
        SetButtonText(binder.btn_ai_analysis, GetAiAnalysisButtonText(replaySystem));
        SetButtonText(binder.btn_ownership, GetOwnershipButtonText(replaySystem));
        RefreshAiAnalysisButtonSelection(hasAiAnalysisRender);
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

        binder.SetSrPlatformState(isPortrait ? ReplayPageUI.SrPlatformState.Portrait : ReplayPageUI.SrPlatformState.Landscape, force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;
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

    private void OnToggleMoveColorAuto(bool isOn)
    {
        if (!isOn) {
            return;
        }

        GetReplaySystem()?.SetTryPlayerFlagOverride(0);
    }

    private void OnToggleMoveColorBlack(bool isOn)
    {
        if (!isOn) {
            return;
        }

        GetReplaySystem()?.SetTryPlayerFlagOverride(PlayerFlag.Player1);
    }

    private void OnToggleMoveColorWhite(bool isOn)
    {
        if (!isOn) {
            return;
        }

        GetReplaySystem()?.SetTryPlayerFlagOverride(PlayerFlag.Player2);
    }

    private void OnClickAiAnalysis()
    {
        ReplaySystem replaySystem = GetReplaySystem();
        if (replaySystem == null) {
            return;
        }

        if (replaySystem.HasAiAnalysisRender) {
            replaySystem.ClearAiAnalysisRender();
            RefreshAiAnalysisButtonSelection(false);
            return;
        }

        if (replaySystem.IsOwnershipAnalyzing || replaySystem.HasOwnershipRender || replaySystem.HasOwnershipResult) {
            replaySystem.ClearOwnershipRender();
        }

        replaySystem.RequestAiAnalysis();
    }

    private void OnClickOwnership()
    {
        ReplaySystem replaySystem = GetReplaySystem();
        if (replaySystem == null) {
            return;
        }

        if (replaySystem.HasOwnershipRender || replaySystem.HasOwnershipResult) {
            replaySystem.ClearOwnershipRender();
            return;
        }

        if (replaySystem.IsAiAnalyzing || replaySystem.HasAiAnalysisRender) {
            replaySystem.ClearAiAnalysisRender();
            RefreshAiAnalysisButtonSelection(false);
        }

        replaySystem.RequestOwnershipAnalysis();
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

        RectTransform rectTransform = GetChartPositionRectTransform();
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

    private void RefreshOwnershipResult(ReplaySystem replaySystem)
    {
        if (binder.panel_replay_shape_result == null) {
            return;
        }

        bool isVisible = replaySystem != null && (replaySystem.IsOwnershipAnalyzing || replaySystem.HasOwnershipResult);
        binder.panel_replay_shape_result.SetActive(isVisible);
        if (!isVisible) {
            return;
        }

        if (replaySystem.IsOwnershipAnalyzing) {
            SetText(binder.txt_shape_lead_points, MessageText.Get("duel_ownership_lead_calculating"));
            SetText(binder.txt_shape_rule_info, MessageText.Get("duel_ownership_rule_calculating"));
            return;
        }

        if (replaySystem.TryGetOwnershipScore(out DuelOwnershipScore score)) {
            SetText(binder.txt_shape_lead_points, DuelOwnershipDisplayFormatter.BuildLeadText(score.blackPoints, score.whitePoints));
            SetText(binder.txt_shape_rule_info, BuildReplayOwnershipRuleInfoText(score.komi));
        }
    }

    private string BuildReplayOwnershipRuleInfoText(float komi)
    {
        SceneComponentReplay compReplay = Global.Instance.sceneManager.mainScene?.GetComponent<SceneComponentReplay>();
        int handicapCount = compReplay != null ? compReplay.replayInitialStones.Count : 0;
        bool isSen = handicapCount <= 0 && Mathf.Approximately(komi, 0.5f);
        return DuelOwnershipDisplayFormatter.BuildRuleInfoText(komi, handicapCount, isSen);
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
        if (binder.img_chart_cursor == null || replaySystem == null || !replaySystem.IsReplayLoaded) {
            if (binder.img_chart_cursor != null) {
                binder.img_chart_cursor.enabled = false;
            }
            return;
        }

        RectTransform chartRect = GetChartPositionRectTransform();
        if (chartRect == null) {
            return;
        }

        RectTransform cursorRect = binder.img_chart_cursor.rectTransform;
        float normalized = replaySystem.ReplayMoveCount <= 0
            ? 0f
            : Mathf.Clamp01((float)Mathf.Clamp(targetMoveIndex, 0, replaySystem.ReplayMoveCount) / replaySystem.ReplayMoveCount);
        float chartLocalX = Mathf.Lerp(chartRect.rect.xMin, chartRect.rect.xMax, normalized);
        float cursorLocalX = ConvertLocalXToAnchoredX(chartRect, chartLocalX, cursorRect);
        cursorRect.anchoredPosition = new Vector2(cursorLocalX, cursorRect.anchoredPosition.y);
        binder.img_chart_cursor.enabled = true;
    }

    private RectTransform GetChartPositionRectTransform()
    {
        if (binder.chart_analysis != null) {
            return binder.chart_analysis.rectTransform;
        }

        if (binder.img_move_scrubber_hit != null) {
            return binder.img_move_scrubber_hit.rectTransform;
        }

        return binder.rect_chart_area;
    }

    private float ConvertLocalXToAnchoredX(RectTransform sourceRect, float sourceLocalX, RectTransform targetRect)
    {
        Transform targetParent = targetRect.parent;
        if (!(targetParent is RectTransform targetParentRect)) {
            return sourceLocalX;
        }

        Vector3 worldPoint = sourceRect.TransformPoint(new Vector3(sourceLocalX, 0f, 0f));
        float parentLocalX = targetParentRect.InverseTransformPoint(worldPoint).x;
        Rect parentRect = targetParentRect.rect;
        float anchorReferenceX = Mathf.Lerp(parentRect.xMin, parentRect.xMax, (targetRect.anchorMin.x + targetRect.anchorMax.x) * 0.5f);
        return parentLocalX - anchorReferenceX;
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

    private string GetOwnershipButtonText(ReplaySystem replaySystem)
    {
        if (replaySystem != null && replaySystem.IsOwnershipAnalyzing) {
            return "计算中";
        }

        if (replaySystem != null && (replaySystem.HasOwnershipRender || replaySystem.HasOwnershipResult)) {
            return MessageText.Get("common_close");
        }

        return MessageText.Get("duel_ownership_button");
    }

    private string FormatPointCount(float pointCount)
    {
        return DuelOwnershipDisplayFormatter.FormatPointCount(pointCount);
    }

    private void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value;
        }
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

    private void RefreshMoveColorButtons(ReplaySystem replaySystem, bool isTryMode)
    {
        PlayerFlag selectedFlag = replaySystem != null ? replaySystem.TryPlayerFlagOverride : 0;
        RefreshMoveColorToggle(binder.toggle_move_color_auto, selectedFlag == 0, isTryMode);
        RefreshMoveColorToggle(binder.toggle_move_color_black, selectedFlag == PlayerFlag.Player1, isTryMode);
        RefreshMoveColorToggle(binder.toggle_move_color_white, selectedFlag == PlayerFlag.Player2, isTryMode);
    }

    private void RefreshMoveColorToggle(Toggle toggle, bool selected, bool interactable)
    {
        if (toggle == null) {
            return;
        }

        toggle.interactable = interactable;
        toggle.SetIsOnWithoutNotify(selected);
    }

    private void RefreshAiAnalysisButtonSelection(bool selected)
    {
        if (binder.btn_ai_analysis == null || EventSystem.current == null) {
            return;
        }

        GameObject buttonGameObject = binder.btn_ai_analysis.gameObject;
        if (selected) {
            if (EventSystem.current.currentSelectedGameObject != buttonGameObject) {
                binder.btn_ai_analysis.Select();
            }
            return;
        }

        if (EventSystem.current.currentSelectedGameObject == buttonGameObject) {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
