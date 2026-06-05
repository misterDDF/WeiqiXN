using TMPro;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using XNClient.Logger;

public class OgsFriendProfilePopup : UIPageWithBinder<OgsFriendProfilePopupUI>
{
    private static OgsFriendListItem pendingItem;
    private static readonly Color AvatarEmptyColor = new Color(0.78f, 0.62f, 0.28f, 1f);

    private OgsFriendListItem currentItem;
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;
    private bool isDeletingFriend;
    private bool isInvitingGame;
    private CancellationTokenSource inviteCancellationTokenSource;
    private RemoteImageView avatarImage;

    public override string pageName => UIPage.GetPageName<OgsFriendProfilePopup>();

    public static void Show(OgsFriendListItem item)
    {
        pendingItem = item;
        Global.Instance.uiManager.ShowPage<OgsFriendProfilePopup>();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        ApplyCurrentLayoutState(true);
        AddButtonListener(binder.btn_close, OnClickClose);
        AddButtonListener(binder.btn_invite_game, OnClickInviteGame);
        AddButtonListener(binder.btn_delete_friend, OnClickDeleteFriend);
        avatarImage = new RemoteImageView(binder, binder.img_avatar, AvatarEmptyColor);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        ApplyCurrentLayoutState(false);
        currentItem = pendingItem;
        pendingItem = null;
        ApplyData(currentItem);
    }

    protected override void OnClose()
    {
        avatarImage?.Clear();
        base.OnClose();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ApplyCurrentLayoutState(false);
    }

    private void ApplyData(OgsFriendListItem item)
    {
        string username = DisplayValue(item?.username, "OGS 好友");

        SetText(binder.txt_username, username);
        SetText(binder.txt_status, DisplayValue(item?.statusText, "状态未知"));
        SetText(binder.txt_user_id, $"OGS ID: {DisplayValue(item?.userId)}");
        SetText(binder.txt_country, $"地区: {DisplayValue(item?.country)}");
        SetText(binder.txt_rating_overall, $"综合评分: {DisplayValue(item?.ratingOverall)}");
        SetText(binder.txt_ranking, $"排名: {DisplayValue(item?.rankingText)}");
        SetText(binder.txt_rating_19, $"19 路: {DisplayValue(item?.rating19)}");
        SetText(binder.txt_rating_13, $"13 路: {DisplayValue(item?.rating13)}");
        SetText(binder.txt_rating_9, $"9 路: {DisplayValue(item?.rating9)}");
        SetText(binder.txt_registered, $"注册时间: {DisplayValue(item?.registeredAt)}");
        SetText(binder.txt_about, $"简介: {DisplayValue(item?.about)}");
        SetText(binder.txt_note, "资料来自 OGS，部分字段可能为空");

        avatarImage?.Load(item?.avatarUrl);
    }

    private void OnClickClose()
    {
        ClosePage();
    }

    private void OnClickInviteGame()
    {
        if (isInvitingGame) {
            return;
        }

        if (!CanUseCurrentFriend(out string friendUserId, out string username)) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasWriteSession) {
            ConfirmPopup.ShowTip("邀请对局", "请先登录 OGS，并确认授权包含对局权限。", null, "确定");
            return;
        }

        DuelSetupPopup.OpenForOgsFriend(duelParams => StartFriendInvite(friendUserId, username, duelParams));
    }

    private void OnClickDeleteFriend()
    {
        if (isDeletingFriend) {
            return;
        }

        if (!CanUseCurrentFriend(out string friendUserId, out string username)) {
            return;
        }

        ConfirmPopup.Show(
            "删除好友",
            $"确定从 OGS 好友列表删除 {username} 吗？",
            () => DeleteFriendAsync(friendUserId, username),
            null,
            "删除",
            "取消");
    }

    private async void DeleteFriendAsync(string friendUserId, string username)
    {
        if (isDeletingFriend) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasWriteSession) {
            ConfirmPopup.ShowTip("删除好友", "请先登录 OGS，并确认授权包含写入权限。", null, "确定");
            return;
        }

        isDeletingFriend = true;
        SetActionButtonsInteractable(false);
        int popupRequestId = ConfirmPopup.ShowBlocking("删除好友", $"正在删除 {username}...");
        try {
            OgsConnectionResult result = await service.DeleteFriendAsync(friendUserId);
            ConfirmPopup.CloseIfOpen(popupRequestId);
            if (!result.success) {
                ConfirmPopup.ShowTip("删除好友失败", result.message, null, "确定");
                return;
            }

            ClosePage();
            OgsFriendListPopup.NotifyFriendDeleted();
            ConfirmPopup.ShowTip("删除好友", $"已删除 {username}。", null, "确定");
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Delete OGS friend from profile popup failed.", ("friendUserId", friendUserId), ("err", ex.Message));
            ConfirmPopup.CloseIfOpen(popupRequestId);
            ConfirmPopup.ShowTip("删除好友失败", ex.Message, null, "确定");
        }
        finally {
            isDeletingFriend = false;
            SetActionButtonsInteractable(true);
        }
    }

    private async void StartFriendInvite(string friendUserId, string username, DuelSceneCreateParamas duelParams)
    {
        if (isInvitingGame || duelParams == null) {
            return;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasWriteSession) {
            ConfirmPopup.ShowTip("邀请对局", "请先登录 OGS，并确认授权包含对局权限。", null, "确定");
            return;
        }

        isInvitingGame = true;
        SetActionButtonsInteractable(false);
        bool canceledByUser = false;
        inviteCancellationTokenSource = new CancellationTokenSource();
        int popupRequestId = ConfirmPopup.ShowCancelableBlocking(
            "邀请对局",
            $"已向 {username} 发起邀请，等待对方接受...",
            () => {
                canceledByUser = true;
                inviteCancellationTokenSource.Cancel();
            },
            "取消邀请");

        try {
            OgsFriendChallengeCreateParams createParams = OgsDuelLaunchFlow.BuildFriendChallengeCreateParams(
                friendUserId,
                duelParams,
                "Friendly Match");
            OgsBotGameStartResult result = await service.CreateFriendChallengeAsync(createParams, cancellationToken: inviteCancellationTokenSource.Token);
            if (!result.success) {
                if (canceledByUser) {
                    return;
                }

                ConfirmPopup.CloseIfOpen(popupRequestId);
                if (string.Equals(result.message, OgsConnectionService.FriendChallengeDeclinedMessage, System.StringComparison.Ordinal)) {
                    ConfirmPopup.ShowTip("邀请被拒绝", OgsConnectionService.FriendChallengeDeclinedMessage, null, "确定");
                    return;
                }

                ConfirmPopup.ShowTip("邀请对局失败", result.message, null, "确定");
                return;
            }

            ConfirmPopup.CloseIfOpen(popupRequestId);
            ConfirmPopup.ShowBlocking("邀请对局", "对方已接受，正在进入棋局...");
            OgsDuelLaunchFlow.EnterOgsDuelScene(result, duelParams);
        }
        catch (System.OperationCanceledException) when (inviteCancellationTokenSource != null && inviteCancellationTokenSource.IsCancellationRequested) {
            ConfirmPopup.CloseIfOpen(popupRequestId);
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Start OGS friend challenge from profile popup failed.", ("friendUserId", friendUserId), ("err", ex.Message));
            ConfirmPopup.CloseIfOpen(popupRequestId);
            ConfirmPopup.ShowTip("邀请对局失败", ex.Message, null, "确定");
        }
        finally {
            inviteCancellationTokenSource?.Dispose();
            inviteCancellationTokenSource = null;
            isInvitingGame = false;
            SetActionButtonsInteractable(true);
        }
    }

    private bool CanUseCurrentFriend(out string friendUserId, out string username)
    {
        friendUserId = DisplayValue(currentItem?.userId, string.Empty);
        username = DisplayValue(currentItem?.username, "该好友");
        if (string.IsNullOrWhiteSpace(friendUserId)) {
            ConfirmPopup.ShowTip("OGS 好友", "该好友缺少 OGS ID，无法执行操作。", null, "确定");
            return false;
        }

        return true;
    }

    private void SetActionButtonsInteractable(bool interactable)
    {
        if (binder.btn_invite_game != null) {
            binder.btn_invite_game.interactable = interactable && !isDeletingFriend && !isInvitingGame;
        }
        if (binder.btn_delete_friend != null) {
            binder.btn_delete_friend.interactable = interactable && !isDeletingFriend && !isInvitingGame;
        }
    }

    private void ApplyCurrentLayoutState(bool force)
    {
        if (binder.sr_platform == null) {
            return;
        }

        RectTransform layoutRoot = binder.panel_root != null ? binder.panel_root : rectTransform;
        bool isPortrait = UIUtils.IsPortrait(layoutRoot);
        if (!force && hasAppliedLayoutState && isPortrait == lastPortraitLayout) {
            return;
        }

        binder.SetSrPlatformState(isPortrait
            ? OgsFriendProfilePopupUI.SrPlatformState.Portrait
            : OgsFriendProfilePopupUI.SrPlatformState.Landscape, force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;
    }

    private static string DisplayValue(string value, string fallback = "--")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value ?? string.Empty;
        }
    }

    private static void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }
}
