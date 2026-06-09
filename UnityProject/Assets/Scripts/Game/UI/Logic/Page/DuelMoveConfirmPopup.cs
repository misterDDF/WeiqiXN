using System;
using UnityEngine.UI;

public class DuelMoveConfirmPopup : UIPageWithBinder<DuelMoveConfirmPopupUI>
{
    private static DuelMoveConfirmPopup openedPopup;
    private static MoveConfirmRequest pendingRequest;
    private static bool isOpening;

    private MoveConfirmRequest currentRequest;

    public override string pageName => UIPage.GetPageName<DuelMoveConfirmPopup>();

    public static void Show(Action onConfirm, Action onCancel, Action<int, int> onAdjust)
    {
        var request = new MoveConfirmRequest(onConfirm, onCancel, onAdjust);
        if (openedPopup != null) {
            openedPopup.currentRequest = request;
            return;
        }

        pendingRequest = request;
        if (isOpening) {
            return;
        }

        isOpening = true;
        Global.Instance.uiManager.ShowPage<DuelMoveConfirmPopup>();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        currentRequest = pendingRequest ?? MoveConfirmRequest.Empty;
        pendingRequest = null;
        isOpening = false;
        openedPopup = this;

        AddButtonListener(binder.btn_confirm, OnClickConfirm);
        AddButtonListener(binder.btn_cancel, OnClickCancel);
        AddButtonListener(binder.btn_move_up, () => currentRequest.onAdjust?.Invoke(0, -1));
        AddButtonListener(binder.btn_move_down, () => currentRequest.onAdjust?.Invoke(0, 1));
        AddButtonListener(binder.btn_move_left, () => currentRequest.onAdjust?.Invoke(-1, 0));
        AddButtonListener(binder.btn_move_right, () => currentRequest.onAdjust?.Invoke(1, 0));
    }

    protected override void OnClose()
    {
        if (openedPopup == this) {
            openedPopup = null;
        }

        pendingRequest = null;
        isOpening = false;
        currentRequest = null;
        base.OnClose();
    }

    private void OnClickConfirm()
    {
        currentRequest?.onConfirm?.Invoke();
        ClosePage();
    }

    private void OnClickCancel()
    {
        currentRequest?.onCancel?.Invoke();
        ClosePage();
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }

    private sealed class MoveConfirmRequest
    {
        public static readonly MoveConfirmRequest Empty = new MoveConfirmRequest(null, null, null);

        public readonly Action onConfirm;
        public readonly Action onCancel;
        public readonly Action<int, int> onAdjust;

        public MoveConfirmRequest(Action onConfirm, Action onCancel, Action<int, int> onAdjust)
        {
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
            this.onAdjust = onAdjust;
        }
    }
}
