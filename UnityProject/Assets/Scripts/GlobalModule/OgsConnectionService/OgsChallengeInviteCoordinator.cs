using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using XNClient.Logger;

public sealed class OgsChallengeInviteCoordinator : ModuleBase
{
    private const float PollIntervalSeconds = 6f;

    private float nextPollTime;
    private bool isPolling;
    private bool isPopupOpen;
    private bool isEnteringGame;
    private HashSet<int> ignoredChallengeIds;
    private CancellationTokenSource cancellationTokenSource;

    public override void Init()
    {
        ignoredChallengeIds = new HashSet<int>();
        cancellationTokenSource = new CancellationTokenSource();
    }

    public override void Update()
    {
        if (!CanPoll()) {
            return;
        }

        if (Time.realtimeSinceStartup < nextPollTime) {
            return;
        }

        nextPollTime = Time.realtimeSinceStartup + PollIntervalSeconds;
        PollInvitesAsync();
    }

    public override void OnDestroy()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        base.OnDestroy();
    }

    private bool CanPoll()
    {
        if (isPolling || isPopupOpen || isEnteringGame) {
            return false;
        }

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null || !service.HasWriteSession) {
            return false;
        }

        string sceneTypeId = Global.Instance.sceneManager?.mainScene?.configData?.id;
        return sceneTypeId == SceneConfig.MAIN_MENU_SCENE_TYPE_ID;
    }

    private async void PollInvitesAsync()
    {
        isPolling = true;
        try {
            OgsChallengeInviteListResult result = await Global.Instance.ogsConnectionService.RequestIncomingChallengeInvitesAsync(cancellationTokenSource.Token);
            if (!result.success || result.invites.Count <= 0) {
                return;
            }

            foreach (OgsChallengeInvite invite in result.invites) {
                if (invite == null || invite.challengeId <= 0 || ignoredChallengeIds.Contains(invite.challengeId)) {
                    continue;
                }

                ShowIncomingInvite(invite);
                break;
            }
        }
        catch (System.OperationCanceledException) when (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested) {
        }
        catch (System.Exception ex) {
            XNLogger.LogWarn("OGS challenge invite polling failed.", ("err", ex.Message));
        }
        finally {
            isPolling = false;
        }
    }

    private void ShowIncomingInvite(OgsChallengeInvite invite)
    {
        isPopupOpen = true;
        string boardText = invite.boardSize > 0 ? $"{invite.boardSize} 路" : "棋盘";
        string content = $"{invite.DisplayName} 邀请你进行 OGS 对局。\n{boardText}";
        ConfirmPopup.Show(
            "OGS 对局邀请",
            content,
            () => AcceptInviteAsync(invite),
            () => {
                ignoredChallengeIds.Add(invite.challengeId);
                isPopupOpen = false;
            },
            "接受",
            "拒绝");
    }

    private async void AcceptInviteAsync(OgsChallengeInvite invite)
    {
        isPopupOpen = false;
        isEnteringGame = true;
        int popupRequestId = ConfirmPopup.ShowBlocking("OGS 对局", "正在建立对局...");
        try {
            OgsBotGameStartResult result = await Global.Instance.ogsConnectionService.AcceptChallengeAsync(invite, cancellationToken: cancellationTokenSource.Token);
            if (!result.success) {
                ConfirmPopup.CloseIfOpen(popupRequestId);
                ConfirmPopup.ShowTip("OGS 对局失败", result.message, null, "确定");
                return;
            }

            ConfirmPopup.UpdateOpenContent(popupRequestId, "OGS 对局", "对局已建立，正在进入棋局...", null, false);
            OgsDuelLaunchFlow.EnterOgsDuelScene(result);
        }
        catch (System.OperationCanceledException) when (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested) {
            ConfirmPopup.CloseIfOpen(popupRequestId);
        }
        catch (System.Exception ex) {
            XNLogger.LogError("OGS challenge invite accept failed.", ("err", ex.Message));
            ConfirmPopup.CloseIfOpen(popupRequestId);
            ConfirmPopup.ShowTip("OGS 对局失败", ex.Message, null, "确定");
        }
        finally {
            isEnteringGame = false;
        }
    }
}
