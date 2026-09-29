using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XNLogger = XNClient.Logger.XNLogger;

public class ConfirmPopup : UIPageWithBinder<ConfirmPopupUI>
{
    private const string DefaultTitleKey = "common_confirm";
    private const string DefaultContent = "";
    private const string DefaultConfirmTextKey = "common_confirm";
    private const string DefaultCancelTextKey = "common_cancel";

    private static ConfirmPopupRequest pendingRequest;
    private static ConfirmPopupRequest pendingUpdateRequest;
    private static ConfirmPopup openedPopup;
    private static int requestSequence;

    private ConfirmPopupRequest currentRequest;
    private Vector2 defaultConfirmButtonPosition;
    private Vector2 defaultConfirmButtonSize;
    private Vector2 defaultCancelButtonPosition;
    private Vector2 defaultCancelButtonSize;
    private ColorBlock defaultConfirmButtonColors;
    private Color defaultCancelPlateColor;
    private RectTransform popupPanel;
    private Vector2 defaultPanelAnchorMin;
    private Vector2 defaultPanelAnchorMax;
    private Vector2 defaultPanelPosition;
    private Vector2 defaultPanelSize;
    private Vector2 defaultPanelPivot;
    private Vector2 defaultTitlePosition;
    private Vector2 defaultTitleSize;
    private Vector2 defaultContentPosition;
    private Vector2 defaultContentSize;
    private float defaultTitleFontSize;
    private float defaultContentFontSize;
    private float defaultContentLineSpacing;
    private TextAlignmentOptions defaultContentAlignment;
    private bool hasCachedButtonLayout;

    public override string pageName => UIPage.GetPageName<ConfirmPopup>();

    public static int Show(
        string title,
        string content,
        Action onConfirm,
        Action onCancel = null,
        string confirmText = null,
        string cancelText = null,
        bool canConfirm = true,
        bool scoreResultLayout = false
    )
    {
        int requestId = ++requestSequence;
        pendingRequest = new ConfirmPopupRequest(requestId, title, content, confirmText, cancelText, onConfirm, onCancel, canConfirm, true, true, scoreResultLayout: scoreResultLayout);
        pendingUpdateRequest = null;
        Global.Instance.uiManager.ShowPage<ConfirmPopup>();
        return requestId;
    }

    public static int ShowTip(
        string title,
        string content,
        Action onConfirm = null,
        string confirmText = null,
        bool canConfirm = true
    )
    {
        int requestId = ++requestSequence;
        pendingRequest = new ConfirmPopupRequest(requestId, title, content, confirmText, null, onConfirm, null, canConfirm, true, false);
        pendingUpdateRequest = null;
        Global.Instance.uiManager.ShowPage<ConfirmPopup>();
        return requestId;
    }

    public static int ShowBlocking(string title, string content)
    {
        int requestId = ++requestSequence;
        pendingRequest = new ConfirmPopupRequest(requestId, title, content, null, null, null, null, false, false, false);
        pendingUpdateRequest = null;
        Global.Instance.uiManager.ShowPage<ConfirmPopup>();
        return requestId;
    }

    public static int ShowCancelableBlocking(string title, string content, Action onCancel, string cancelText = null)
    {
        int requestId = ++requestSequence;
        pendingRequest = new ConfirmPopupRequest(requestId, title, content, null, cancelText, null, onCancel, false, false, true);
        pendingUpdateRequest = null;
        Global.Instance.uiManager.ShowPage<ConfirmPopup>();
        return requestId;
    }

    public static int ShowInput(
        string title,
        string content,
        string inputText,
        Action<string> onConfirm,
        Action onCancel = null,
        string confirmText = null,
        string cancelText = null,
        bool canConfirm = true
    )
    {
        int requestId = ++requestSequence;
        pendingRequest = new ConfirmPopupRequest(
            requestId,
            title,
            content,
            confirmText,
            cancelText,
            null,
            onCancel,
            canConfirm,
            true,
            true,
            true,
            inputText,
            onConfirm
        );
        pendingUpdateRequest = null;
        Global.Instance.uiManager.ShowPage<ConfirmPopup>();
        return requestId;
    }

    public static bool CloseIfOpen(int requestId)
    {
        if (requestId <= 0) {
            return false;
        }

        if (openedPopup != null && openedPopup.currentRequest != null && openedPopup.currentRequest.requestId == requestId) {
            openedPopup.ClosePage();
            return true;
        }

        if (pendingRequest != null && pendingRequest.requestId == requestId) {
            pendingRequest = null;
            pendingUpdateRequest = null;
            return true;
        }

        return false;
    }

    public static void CloseSceneExitRequests()
    {
        if (pendingRequest != null) {
            pendingRequest = null;
        }
        pendingUpdateRequest = null;
    }

    public static void UpdateOpenContent(string title, string content, Action onConfirm, bool canConfirm = true, bool scoreResultLayout = false)
    {
        int requestId = openedPopup?.currentRequest?.requestId ?? pendingRequest?.requestId ?? 0;
        UpdateOpenContent(requestId, title, content, onConfirm, canConfirm, scoreResultLayout);
    }

    public static void UpdateOpenContent(int requestId, string title, string content, Action onConfirm, bool canConfirm = true, bool scoreResultLayout = false)
    {
        if (requestId <= 0 || !CanUpdateRequest(requestId)) {
            return;
        }

        ConfirmPopupRequest current = openedPopup?.currentRequest ?? pendingRequest;
        pendingUpdateRequest = new ConfirmPopupRequest(
            requestId,
            title,
            content,
            current?.confirmText ?? DefaultConfirmText,
            current?.cancelText ?? DefaultCancelText,
            onConfirm,
            current?.onCancel,
            canConfirm,
            current == null || current.showConfirmButton,
            current == null || current.showCancelButton,
            current != null && current.showInput,
            current?.inputText,
            current?.onInputConfirm,
            scoreResultLayout
        );
        openedPopup?.ApplyPendingUpdate();
        openedPopup?.RefreshContent();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        if (!IsBinderReady()) {
            XNLogger.LogError("ConfirmPopup prefab binder reference is incomplete.");
            return;
        }

        AddButtonListener(binder.btn_confirm, OnClickBtnConfirm);
        AddButtonListener(binder.btn_cancel, OnClickBtnCancel);
        CacheButtonLayout();
        CacheContentLayout();
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        currentRequest = pendingRequest ?? ConfirmPopupRequest.Empty;
        pendingRequest = null;
        openedPopup = this;
        ApplyPendingUpdate();
        RefreshContent();
    }

    protected override void OnClose()
    {
        if (openedPopup == this) {
            openedPopup = null;
        }
        pendingUpdateRequest = null;
        currentRequest = null;

        base.OnClose();
    }

    private void OnClickBtnConfirm()
    {
        if (currentRequest == null || !currentRequest.showConfirmButton || !currentRequest.canConfirm) {
            return;
        }

        Action callback = currentRequest?.onConfirm;
        Action<string> inputCallback = currentRequest?.onInputConfirm;
        string inputText = binder.input_content != null ? binder.input_content.text : string.Empty;
        ClosePage();
        callback?.Invoke();
        inputCallback?.Invoke(inputText);
    }

    private void OnClickBtnCancel()
    {
        if (currentRequest == null || !currentRequest.showCancelButton) {
            return;
        }

        Action callback = currentRequest?.onCancel;
        ClosePage();
        callback?.Invoke();
    }

    private void RefreshContent()
    {
        bool showScoreResult = currentRequest != null && currentRequest.scoreResultLayout;
        SetText(binder.txt_title, showScoreResult ? "对局  ·  数子" : currentRequest?.title ?? DefaultTitle);
        SetText(binder.txt_content, currentRequest?.content ?? DefaultContent);
        SetText(binder.txt_confirm, currentRequest?.confirmText ?? DefaultConfirmText);
        SetText(binder.txt_cancel, currentRequest?.cancelText ?? DefaultCancelText);
        SetInputVisible(currentRequest != null && currentRequest.showInput, currentRequest?.inputText ?? string.Empty);
        SetConfirmInteractable(currentRequest == null || currentRequest.canConfirm);
        bool showConfirmButton = currentRequest == null || currentRequest.showConfirmButton;
        bool showCancelButton = currentRequest == null || currentRequest.showCancelButton;
        SetConfirmVisible(showConfirmButton);
        SetCancelVisible(showCancelButton, showConfirmButton);
        ApplyContentLayout(showScoreResult);
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

    private void SetConfirmInteractable(bool canConfirm)
    {
        if (binder.btn_confirm != null) {
            binder.btn_confirm.interactable = canConfirm;
        }
    }

    private void SetConfirmVisible(bool visible)
    {
        if (binder.btn_confirm != null) {
            binder.btn_confirm.gameObject.SetActive(visible);
        }
    }

    private void SetCancelVisible(bool visible, bool showConfirmButton)
    {
        if (binder.btn_cancel != null) {
            binder.btn_cancel.gameObject.SetActive(visible);
        }

        ApplyButtonLayout(visible, showConfirmButton);
    }

    private void SetInputVisible(bool visible, string value)
    {
        if (binder.input_content == null) {
            return;
        }

        binder.input_content.gameObject.SetActive(visible);
        if (visible) {
            binder.input_content.SetTextWithoutNotify(value ?? string.Empty);
            binder.input_content.Select();
            binder.input_content.ActivateInputField();
        }
    }

    private void CacheButtonLayout()
    {
        if (hasCachedButtonLayout || binder.btn_confirm == null || binder.btn_cancel == null) {
            return;
        }

        RectTransform confirmRect = binder.btn_confirm.transform as RectTransform;
        RectTransform cancelRect = binder.btn_cancel.transform as RectTransform;
        if (confirmRect == null || cancelRect == null) {
            return;
        }

        defaultConfirmButtonPosition = confirmRect.anchoredPosition;
        defaultConfirmButtonSize = confirmRect.sizeDelta;
        defaultCancelButtonPosition = cancelRect.anchoredPosition;
        defaultCancelButtonSize = cancelRect.sizeDelta;
        defaultConfirmButtonColors = binder.btn_confirm.colors;
        if (binder.btn_cancel.targetGraphic != null) {
            defaultCancelPlateColor = binder.btn_cancel.targetGraphic.color;
        }
        hasCachedButtonLayout = true;
    }

    private void CacheContentLayout()
    {
        popupPanel = binder.txt_content.rectTransform.parent as RectTransform;
        if (popupPanel == null) {
            return;
        }

        defaultPanelSize = popupPanel.sizeDelta;
        defaultPanelAnchorMin = popupPanel.anchorMin;
        defaultPanelAnchorMax = popupPanel.anchorMax;
        defaultPanelPosition = popupPanel.anchoredPosition;
        defaultPanelPivot = popupPanel.pivot;
        defaultTitlePosition = binder.txt_title.rectTransform.anchoredPosition;
        defaultTitleSize = binder.txt_title.rectTransform.sizeDelta;
        defaultContentPosition = binder.txt_content.rectTransform.anchoredPosition;
        defaultContentSize = binder.txt_content.rectTransform.sizeDelta;
        defaultTitleFontSize = binder.txt_title.fontSize;
        defaultContentFontSize = binder.txt_content.fontSize;
        defaultContentLineSpacing = binder.txt_content.lineSpacing;
        defaultContentAlignment = binder.txt_content.alignment;
    }

    private void ApplyContentLayout(bool showScoreResult)
    {
        if (popupPanel == null) {
            return;
        }

        bool isPortraitScore = showScoreResult && Screen.height > Screen.width;
        popupPanel.anchorMin = isPortraitScore ? Vector2.zero : defaultPanelAnchorMin;
        popupPanel.anchorMax = isPortraitScore ? new Vector2(1f, 0f) : defaultPanelAnchorMax;
        popupPanel.pivot = isPortraitScore ? new Vector2(0.5f, 0f) : defaultPanelPivot;
        popupPanel.anchoredPosition = isPortraitScore ? Vector2.zero : defaultPanelPosition;
        popupPanel.sizeDelta = isPortraitScore ? new Vector2(0f, 480f) : showScoreResult ? new Vector2(600f, 440f) : defaultPanelSize;
        binder.txt_title.rectTransform.anchoredPosition = showScoreResult ? new Vector2(34f, -48f) : defaultTitlePosition;
        binder.txt_title.rectTransform.sizeDelta = showScoreResult ? new Vector2(-118f, 26f) : defaultTitleSize;
        binder.txt_title.fontSize = showScoreResult ? 13f : defaultTitleFontSize;
        binder.txt_content.rectTransform.anchoredPosition = showScoreResult ? new Vector2(0f, isPortraitScore ? 0f : -4f) : defaultContentPosition;
        binder.txt_content.rectTransform.sizeDelta = showScoreResult ? new Vector2(isPortraitScore ? 640f : 500f, 300f) : defaultContentSize;
        binder.txt_content.fontSize = showScoreResult ? 18f : defaultContentFontSize;
        binder.txt_content.lineSpacing = showScoreResult ? 12f : defaultContentLineSpacing;
        binder.txt_content.alignment = showScoreResult ? TextAlignmentOptions.TopLeft : defaultContentAlignment;

        if (!hasCachedButtonLayout) {
            return;
        }

        RectTransform confirmRect = binder.btn_confirm.transform as RectTransform;
        RectTransform cancelRect = binder.btn_cancel.transform as RectTransform;
        if (showScoreResult) {
            confirmRect.anchoredPosition = isPortraitScore ? new Vector2(160f, -185f) : new Vector2(220f, -170f);
            confirmRect.sizeDelta = new Vector2(isPortraitScore ? 300f : 160f, 52f);
            cancelRect.anchoredPosition = isPortraitScore ? new Vector2(-160f, -185f) : new Vector2(92f, -170f);
            cancelRect.sizeDelta = new Vector2(isPortraitScore ? 300f : 112f, 52f);
            ColorBlock scoreColors = defaultConfirmButtonColors;
            scoreColors.normalColor = UIPalette.Ink;
            scoreColors.highlightedColor = Color.Lerp(UIPalette.Ink, Color.white, 0.08f);
            scoreColors.pressedColor = Color.Lerp(UIPalette.Ink, Color.black, 0.08f);
            scoreColors.selectedColor = UIPalette.Ink;
            binder.btn_confirm.colors = scoreColors;
            if (binder.btn_cancel.targetGraphic != null) {
                binder.btn_cancel.targetGraphic.color = isPortraitScore ? defaultCancelPlateColor : Color.clear;
            }
        } else {
            cancelRect.sizeDelta = defaultCancelButtonSize;
            binder.btn_confirm.colors = defaultConfirmButtonColors;
            if (binder.btn_cancel.targetGraphic != null) {
                binder.btn_cancel.targetGraphic.color = defaultCancelPlateColor;
            }
        }
    }

    private void ApplyButtonLayout(bool showCancelButton, bool showConfirmButton)
    {
        if (!hasCachedButtonLayout || binder.btn_confirm == null || binder.btn_cancel == null) {
            return;
        }

        RectTransform confirmRect = binder.btn_confirm.transform as RectTransform;
        RectTransform cancelRect = binder.btn_cancel.transform as RectTransform;
        if (confirmRect == null || cancelRect == null) {
            return;
        }

        if (!showConfirmButton) {
            if (showCancelButton) {
                cancelRect.anchoredPosition = new Vector2(0f, defaultCancelButtonPosition.y);
            }
            return;
        }

        if (showCancelButton) {
            confirmRect.anchoredPosition = defaultConfirmButtonPosition;
            confirmRect.sizeDelta = defaultConfirmButtonSize;
            cancelRect.anchoredPosition = defaultCancelButtonPosition;
            return;
        }

        confirmRect.anchoredPosition = new Vector2(0f, defaultConfirmButtonPosition.y);
        confirmRect.sizeDelta = new Vector2(Mathf.Max(defaultConfirmButtonSize.x, 190f), defaultConfirmButtonSize.y);
    }

    private void ApplyPendingUpdate()
    {
        if (pendingUpdateRequest == null || currentRequest == null || pendingUpdateRequest.requestId != currentRequest.requestId) {
            return;
        }

        currentRequest = pendingUpdateRequest;
        pendingUpdateRequest = null;
    }

    private static bool CanUpdateRequest(int requestId)
    {
        if (openedPopup != null) {
            return openedPopup.currentRequest != null && openedPopup.currentRequest.requestId == requestId;
        }

        return pendingRequest != null && pendingRequest.requestId == requestId;
    }

    private bool IsBinderReady()
    {
        return binder != null
            && binder.txt_title != null
            && binder.txt_content != null
            && binder.input_content != null
            && binder.txt_confirm != null
            && binder.txt_cancel != null
            && binder.btn_confirm != null
            && binder.btn_cancel != null;
    }

    private class ConfirmPopupRequest
    {
        public static readonly ConfirmPopupRequest Empty = new ConfirmPopupRequest(
            0,
            DefaultTitle,
            DefaultContent,
            DefaultConfirmText,
            DefaultCancelText,
            null,
            null,
            true,
            true,
            true,
            false,
            string.Empty,
            null
        );

        public readonly int requestId;
        public readonly string title;
        public readonly string content;
        public readonly string confirmText;
        public readonly string cancelText;
        public readonly Action onConfirm;
        public readonly Action onCancel;
        public readonly bool canConfirm;
        public readonly bool showConfirmButton;
        public readonly bool showCancelButton;
        public readonly bool showInput;
        public readonly string inputText;
        public readonly Action<string> onInputConfirm;
        public readonly bool scoreResultLayout;

        public ConfirmPopupRequest(
            int requestId,
            string title,
            string content,
            string confirmText,
            string cancelText,
            Action onConfirm,
            Action onCancel,
            bool canConfirm,
            bool showConfirmButton,
            bool showCancelButton,
            bool showInput = false,
            string inputText = null,
            Action<string> onInputConfirm = null,
            bool scoreResultLayout = false
        )
        {
            this.requestId = requestId;
            this.title = string.IsNullOrEmpty(title) ? DefaultTitle : title;
            this.content = content ?? DefaultContent;
            this.confirmText = string.IsNullOrEmpty(confirmText) ? DefaultConfirmText : confirmText;
            this.cancelText = string.IsNullOrEmpty(cancelText) ? DefaultCancelText : cancelText;
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
            this.canConfirm = canConfirm;
            this.showConfirmButton = showConfirmButton;
            this.showCancelButton = showCancelButton;
            this.showInput = showInput;
            this.inputText = inputText ?? string.Empty;
            this.onInputConfirm = onInputConfirm;
            this.scoreResultLayout = scoreResultLayout;
        }
    }

    private static string DefaultTitle => MessageText.Get(DefaultTitleKey);
    private static string DefaultConfirmText => MessageText.Get(DefaultConfirmTextKey);
    private static string DefaultCancelText => MessageText.Get(DefaultCancelTextKey);
}
