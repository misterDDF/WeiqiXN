using System;
using TMPro;
using UnityEngine.UI;
using XNLogger = XNClient.Logger.XNLogger;

public class ConfirmPopup : UIPageWithBinder<ConfirmPopupUI>
{
    private const string DefaultTitle = "确认";
    private const string DefaultContent = "";
    private const string DefaultConfirmText = "确认";
    private const string DefaultCancelText = "取消";

    private static ConfirmPopupRequest pendingRequest;

    private ConfirmPopupRequest currentRequest;

    public override string pageName => UIPage.GetPageName<ConfirmPopup>();

    public static void Show(
        string title,
        string content,
        Action onConfirm,
        Action onCancel = null,
        string confirmText = DefaultConfirmText,
        string cancelText = DefaultCancelText
    )
    {
        pendingRequest = new ConfirmPopupRequest(title, content, confirmText, cancelText, onConfirm, onCancel);
        Global.Instance.uiManager.ShowPage<ConfirmPopup>();
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
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        currentRequest = pendingRequest ?? ConfirmPopupRequest.Empty;
        pendingRequest = null;
        RefreshContent();
    }

    protected override void OnClose()
    {
        currentRequest = null;

        base.OnClose();
    }

    private void OnClickBtnConfirm()
    {
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
            DefaultTitle,
            DefaultContent,
            DefaultConfirmText,
            DefaultCancelText,
            null,
            null
        );

        public readonly string title;
        public readonly string content;
        public readonly string confirmText;
        public readonly string cancelText;
        public readonly Action onConfirm;
        public readonly Action onCancel;

        public ConfirmPopupRequest(
            string title,
            string content,
            string confirmText,
            string cancelText,
            Action onConfirm,
            Action onCancel
        )
        {
            this.title = string.IsNullOrEmpty(title) ? DefaultTitle : title;
            this.content = content ?? DefaultContent;
            this.confirmText = string.IsNullOrEmpty(confirmText) ? DefaultConfirmText : confirmText;
            this.cancelText = string.IsNullOrEmpty(cancelText) ? DefaultCancelText : cancelText;
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
        }
    }
}
