using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XNLogger = XNClient.Logger.XNLogger;

public class ConfirmPopup : UIPageWithBinder<ConfirmPopupUI>
{
    private const string DefaultTitle = "确认";
    private const string DefaultContent = "";
    private const string DefaultConfirmText = "确认";
    private const string DefaultCancelText = "取消";

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
        string confirmText = DefaultConfirmText,
        string cancelText = DefaultCancelText,
        bool canConfirm = true
    )
    {
        int requestId = ++requestSequence;
        pendingRequest = new ConfirmPopupRequest(requestId, title, content, confirmText, cancelText, onConfirm, onCancel, canConfirm, true);
        pendingUpdateRequest = null;
        Global.Instance.uiManager.ShowPage<ConfirmPopup>();
        return requestId;
    }

    public static int ShowTip(
        string title,
        string content,
        Action onConfirm = null,
        string confirmText = DefaultConfirmText,
        bool canConfirm = true
    )
    {
        int requestId = ++requestSequence;
        pendingRequest = new ConfirmPopupRequest(requestId, title, content, confirmText, DefaultCancelText, onConfirm, null, canConfirm, false);
        pendingUpdateRequest = null;
        Global.Instance.uiManager.ShowPage<ConfirmPopup>();
        return requestId;
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
            current == null || current.showCancelButton
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
        if (currentRequest == null || !currentRequest.canConfirm) {
            return;
        }

        Action callback = currentRequest?.onConfirm;
        ClosePage();
        callback?.Invoke();
    }

    private void OnClickBtnCancel()
    {
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
        SetConfirmInteractable(currentRequest == null || currentRequest.canConfirm);
        SetCancelVisible(currentRequest == null || currentRequest.showCancelButton);
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

    private void SetCancelVisible(bool visible)
    {
        if (binder.btn_cancel != null) {
            binder.btn_cancel.gameObject.SetActive(visible);
        }

        ApplyButtonLayout(visible);
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

    private void ApplyButtonLayout(bool showCancelButton)
    {
        if (!hasCachedButtonLayout || binder.btn_confirm == null || binder.btn_cancel == null) {
            return;
        }

        RectTransform confirmRect = binder.btn_confirm.transform as RectTransform;
        RectTransform cancelRect = binder.btn_cancel.transform as RectTransform;
        if (confirmRect == null || cancelRect == null) {
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
            true
        );

        public readonly int requestId;
        public readonly string title;
        public readonly string content;
        public readonly string confirmText;
        public readonly string cancelText;
        public readonly Action onConfirm;
        public readonly Action onCancel;
        public readonly bool canConfirm;
        public readonly bool showCancelButton;

        public ConfirmPopupRequest(
            int requestId,
            string title,
            string content,
            string confirmText,
            string cancelText,
            Action onConfirm,
            Action onCancel,
            bool canConfirm,
            bool showCancelButton
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
            this.showCancelButton = showCancelButton;
        }
    }
}
