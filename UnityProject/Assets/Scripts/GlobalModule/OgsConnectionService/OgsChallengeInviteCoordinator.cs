using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using XNClient.Logger;

public sealed class OgsChallengeInviteCoordinator : ModuleBase
{
    private const float PollIntervalSeconds = 2f;

    private float nextPollTime;
    private bool isPolling;
    private bool isPopupOpen;
    private bool isEnteringGame;
    private HashSet<int> ignoredChallengeIds;
    private CancellationTokenSource cancellationTokenSource;

    public OgsChallengeInviteCoordinator()
    {
        EnsureInitialized();
    }

    public override void Init()
    {
        EnsureInitialized();
    }

    public override void Update()
    {
        EnsureInitialized();
        if (!CanPoll()) {
            return;
        }

        if (Time.realtimeSinceStartup < nextPollTime) {
            return;
        }

        nextPollTime = Time.realtimeSinceStartup + PollIntervalSeconds;
        PollInvitesAsync();
    }

    public void RequestImmediatePoll()
    {
        EnsureInitialized();
        nextPollTime = 0f;
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
        if (service == null || !service.HasSession) {
            return false;
        }

        string sceneTypeId = Global.Instance.sceneManager?.mainScene?.configData?.id;
        return CanPollInScene(sceneTypeId);
    }

    private bool CanPollInScene(string sceneTypeId)
    {
        return sceneTypeId == SceneConfig.MAIN_MENU_SCENE_TYPE_ID
            || sceneTypeId == SceneConfig.DUEL_SCENE_TYPE_ID
            || sceneTypeId == SceneConfig.REPLAY_SCENE_TYPE_ID
            || sceneTypeId == SceneConfig.OGS_DUEL_SCENE_TYPE_ID;
    }

    private async void PollInvitesAsync()
    {
        EnsureInitialized();
        isPolling = true;
        try {
            OgsChallengeInviteListResult result = await Global.Instance.ogsConnectionService.RequestIncomingChallengeInvitesAsync(cancellationTokenSource.Token);
            if (!result.success || result.invites.Count <= 0) {
                return;
            }

            XNLogger.LogInfo("OGS challenge invites found.", ("count", result.invites.Count.ToString()));
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
            () => DeclineInviteAsync(invite),
            "接受",
            "拒绝");
    }

    private async void DeclineInviteAsync(OgsChallengeInvite invite)
    {
        if (invite == null || invite.challengeId <= 0) {
            isPopupOpen = false;
            return;
        }

        ignoredChallengeIds.Add(invite.challengeId);
        isPopupOpen = false;

        OgsConnectionService service = Global.Instance.ogsConnectionService;
        if (service == null) {
            XNLogger.LogWarn("OGS challenge invite decline skipped.", ("challengeId", invite.challengeId.ToString()), ("err", "OGS connection service is unavailable."));
            return;
        }

        try {
            OgsConnectionResult result = await service.CancelChallengeAsync(invite.challengeId, cancellationTokenSource.Token);
            if (result.success) {
                XNLogger.LogInfo("OGS challenge invite declined.", ("challengeId", invite.challengeId.ToString()));
                return;
            }

            XNLogger.LogWarn("OGS challenge invite decline failed.", ("challengeId", invite.challengeId.ToString()), ("err", result.message));
        }
        catch (System.OperationCanceledException) when (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested) {
        }
        catch (System.Exception ex) {
            XNLogger.LogWarn("OGS challenge invite decline failed.", ("challengeId", invite.challengeId.ToString()), ("err", ex.Message));
        }
    }

    private async void AcceptInviteAsync(OgsChallengeInvite invite)
    {
        isPopupOpen = false;
        isEnteringGame = true;
        int popupRequestId = 0;
        try {
            await PrepareCurrentSceneForAcceptedInviteAsync();
            popupRequestId = ConfirmPopup.ShowBlocking("OGS 对局", "正在建立对局...");
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

    private async Task PrepareCurrentSceneForAcceptedInviteAsync()
    {
        SceneBase mainScene = Global.Instance.sceneManager?.mainScene;
        string sceneTypeId = mainScene?.configData?.id;
        if (sceneTypeId == SceneConfig.DUEL_SCENE_TYPE_ID) {
            SubmitCurrentDuelResign(mainScene);
            EnterMainMenu();
            return;
        }

        if (sceneTypeId == SceneConfig.OGS_DUEL_SCENE_TYPE_ID) {
            await SubmitCurrentOgsDuelResignAsync(mainScene);
            EnterMainMenu();
            return;
        }

        if (sceneTypeId == SceneConfig.REPLAY_SCENE_TYPE_ID) {
            EnterMainMenu();
        }
    }

    private void SubmitCurrentDuelResign(SceneBase mainScene)
    {
        DuelSystem duelSystem = mainScene?.GetSystem<DuelSystem>();
        bool submitted = duelSystem != null && duelSystem.SubmitLocalInterruptResign();
        if (!submitted) {
            XNLogger.LogWarn("Duel interrupt resign skipped before accepting OGS invite.");
        }

        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.isLanDuel.value) {
            Global.Instance.lanRoomService?.LeaveCurrentSession(LanRoomLeaveReason.ExitDuel, false);
        }
    }

    private async Task SubmitCurrentOgsDuelResignAsync(SceneBase mainScene)
    {
        OgsDuelSystem ogsDuelSystem = mainScene?.GetSystem<OgsDuelSystem>();
        bool submitted = false;
        if (ogsDuelSystem != null) {
            submitted = await ogsDuelSystem.SubmitInterruptResignAsync();
        }
        if (!submitted) {
            XNLogger.LogWarn("OGS duel interrupt resign skipped before accepting OGS invite.");
        }
    }

    private void EnterMainMenu()
    {
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
    }

    private void EnsureInitialized()
    {
        if (ignoredChallengeIds == null) {
            ignoredChallengeIds = new HashSet<int>();
        }
        if (cancellationTokenSource == null) {
            cancellationTokenSource = new CancellationTokenSource();
        }
    }
}
