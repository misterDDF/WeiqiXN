using System;
using UnityEngine.UI;

public class DuelMoveConfirmPopup : UIPageWithBinder<DuelMoveConfirmPopupUI>
{
    private static DuelMoveConfirmPopup openedPopup;
    private static MoveConfirmRequest pendingRequest;
    private static bool isOpening;

    private MoveConfirmRequest currentRequest;

    public override string pageName => UIPage.GetPageName<DuelMoveConfirmPopup>();

    public static void Show(Action onConfirm, Action onCancel, Action<int, int> onAdjust, Func<string> getCoordinateText)
    {
        var request = new MoveConfirmRequest(onConfirm, onCancel, onAdjust, getCoordinateText);
        if (openedPopup != null) {
            openedPopup.currentRequest = request;
            openedPopup.RefreshCoordinateText();
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
        AddButtonListener(binder.btn_move_up, () => AdjustMove(0, -1));
        AddButtonListener(binder.btn_move_down, () => AdjustMove(0, 1));
        AddButtonListener(binder.btn_move_left, () => AdjustMove(-1, 0));
        AddButtonListener(binder.btn_move_right, () => AdjustMove(1, 0));
        RefreshCoordinateText();
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

    public override bool TryHandleBackNavigation()
    {
        if (!isLoaded || !isVisible) {
            return false;
        }

        OnClickCancel();
        return true;
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

    private void AdjustMove(int offsetX, int offsetZ)
    {
        currentRequest.onAdjust?.Invoke(offsetX, offsetZ);
        RefreshCoordinateText();
    }

    private void RefreshCoordinateText()
    {
        if (binder.txt_move_coordinate != null) {
            binder.txt_move_coordinate.text = currentRequest?.getCoordinateText?.Invoke() ?? "--";
        }
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }

    private sealed class MoveConfirmRequest
    {
        public static readonly MoveConfirmRequest Empty = new MoveConfirmRequest(null, null, null, null);

        public readonly Action onConfirm;
        public readonly Action onCancel;
        public readonly Action<int, int> onAdjust;
        public readonly Func<string> getCoordinateText;

        public MoveConfirmRequest(Action onConfirm, Action onCancel, Action<int, int> onAdjust, Func<string> getCoordinateText)
        {
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
            this.onAdjust = onAdjust;
            this.getCoordinateText = getCoordinateText;
        }
    }
}
