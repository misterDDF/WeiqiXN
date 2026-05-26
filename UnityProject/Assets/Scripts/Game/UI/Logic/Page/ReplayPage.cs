using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ReplayPage : UIPageWithBinder<ReplayPageUI>
{
    private const float HudPanelAlpha = 0.72f;
    private const float RootPanelAlpha = 0f;
    private bool hudLayoutApplied;

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
        ApplyHudLayout();
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
            SetPanelImage(sidePanel, new Color(0.08f, 0.08f, 0.08f, HudPanelAlpha), true);
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
            SetRect(closeRect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-64f, -28f), new Vector2(96f, 38f));
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
