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
    private bool hasCachedButtonLayout;

    public override string pageName => UIPage.GetPageName<ConfirmPopup>();

    public static int Show(
        string title,
        string content,
        Action onConfirm,
        Action onCancel = null,
        string confirmText = null,
        string cancelText = null,
        bool canConfirm = true
    )
    {
        int requestId = ++requestSequence;
        pendingRequest = new ConfirmPopupRequest(requestId, title, content, confirmText, cancelText, onConfirm, onCancel, canConfirm, true, true);
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

    public static void UpdateOpenContent(string title, string content, Action onConfirm, bool canConfirm = true)
    {
        int requestId = openedPopup?.currentRequest?.requestId ?? pendingRequest?.requestId ?? 0;
        UpdateOpenContent(requestId, title, content, onConfirm, canConfirm);
    }

    public static void UpdateOpenContent(int requestId, string title, string content, Action onConfirm, bool canConfirm = true)
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
            current?.onInputConfirm
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
        SetText(binder.txt_title, currentRequest?.title ?? DefaultTitle);
        SetText(binder.txt_content, currentRequest?.content ?? DefaultContent);
        SetText(binder.txt_confirm, currentRequest?.confirmText ?? DefaultConfirmText);
        SetText(binder.txt_cancel, currentRequest?.cancelText ?? DefaultCancelText);
        SetInputVisible(currentRequest != null && currentRequest.showInput, currentRequest?.inputText ?? string.Empty);
        SetConfirmInteractable(currentRequest == null || currentRequest.canConfirm);
        bool showConfirmButton = currentRequest == null || currentRequest.showConfirmButton;
        bool showCancelButton = currentRequest == null || currentRequest.showCancelButton;
        SetConfirmVisible(showConfirmButton);
        SetCancelVisible(showCancelButton, showConfirmButton);
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
        hasCachedButtonLayout = true;
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
            Action<string> onInputConfirm = null
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
        }
    }

    private static string DefaultTitle => MessageText.Get(DefaultTitleKey);
    private static string DefaultConfirmText => MessageText.Get(DefaultConfirmTextKey);
    private static string DefaultCancelText => MessageText.Get(DefaultCancelTextKey);
}
