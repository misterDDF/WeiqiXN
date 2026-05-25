using System;
using System.IO;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

public class DuelReplayArchiveSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelReplayArchiveSystem>();

    public DuelReplayArchiveSystem(DuelScene scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();

        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnDuelPassAccepted>(OnDuelPassAccepted);
        scene.RegisterSystemEvent<OnDuelScoreFailed>(OnDuelScoreFailed);
        scene.RegisterSystemEvent<OnDuelTakeBackResult>(OnDuelTakeBackResult);
        scene.RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);
    }

    private void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        WriteReplayArchive(false);
    }

    private void OnDuelPassAccepted(OnDuelPassAccepted evt)
    {
        WriteReplayArchive(false);
    }

    private void OnDuelTakeBackResult(OnDuelTakeBackResult evt)
    {
        if (evt != null && evt.success && evt.removedMoveCount > 0) {
            WriteReplayArchive(false);
        }
    }

    private void OnDuelScoreFailed(OnDuelScoreFailed evt)
    {
        WriteReplayArchive(false);
    }

    private void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        if (evt != null && evt.curStateName == DuelStateDefine.STATE_GAME_END) {
            WriteReplayArchive(true);
        }
    }

    private bool WriteReplayArchive(bool isCompleted)
    {
        var duelScene = scene as DuelScene;
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (duelScene == null || compDuel == null) {
            return false;
        }

        int moveCount = DuelMoveHistory.Count(compDuel.kataGoMoves);
        if (moveCount <= 0) {
            return false;
        }

        EnsureReplayIdentity(compDuel);
        bool isArchived = moveCount >= DuelReplayIndexFile.MinArchivedMoveCount;
        if (isCompleted && !isArchived) {
            DuelReplayIndexFile.Remove(compDuel.replayGameId.value);
            DeleteReplayDraft(compDuel.replayGameId.value);
            return true;
        }

        string gameId = compDuel.replayGameId.value;
        string recordPath = GameSaveConfig.GetReplayDuelRecordPath(gameId);
        string saveInfoPath = GameSaveConfig.GetReplayDuelSaveInfoPath(gameId);
        string scenePath = GameSaveConfig.GetReplayDuelScenePath(gameId);

        if (!KataGoDuelRecordFile.Save(duelScene, recordPath)) {
            XNLogger.LogError("Duel replay archive record save failed.", ("gameId", gameId), ("filePath", recordPath));
            return false;
        }

        if (isCompleted && string.IsNullOrEmpty(compDuel.replayArchivedAtUtc.value)) {
            compDuel.replayArchivedAtUtc.value = DateTime.UtcNow.ToString("o");
        }

        JObject saveInfoJson = DuelSaveInfoFile.BuildSaveInfoJson(
            duelScene,
            -1,
            isArchived,
            isCompleted,
            compDuel.replayArchivedAtUtc.value);
        if (!DuelSaveInfoFile.Save(
            duelScene,
            saveInfoPath,
            -1,
            isArchived,
            isCompleted,
            compDuel.replayArchivedAtUtc.value)) {
            XNLogger.LogError("Duel replay archive save info save failed.", ("gameId", gameId), ("filePath", saveInfoPath));
            return false;
        }

        if (!Global.Instance.gameSaveManager.SaveData(scene, scenePath)) {
            XNLogger.LogError("Duel replay archive scene save failed.", ("gameId", gameId), ("filePath", scenePath));
            return false;
        }

        if (isArchived) {
            DuelReplayIndexFile.Upsert(saveInfoJson);
        } else {
            DuelReplayIndexFile.Remove(gameId);
        }

        return true;
    }

    private void EnsureReplayIdentity(SceneComponentDuel compDuel)
    {
        string nowUtc = DateTime.UtcNow.ToString("o");
        if (string.IsNullOrEmpty(compDuel.replayGameId.value)) {
            compDuel.replayGameId.value = CreateGameId();
        }

        if (string.IsNullOrEmpty(compDuel.replayCreatedAtUtc.value)) {
            compDuel.replayCreatedAtUtc.value = nowUtc;
        }

        compDuel.replayLastUpdatedAtUtc.value = nowUtc;
    }

    private string CreateGameId()
    {
        return $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
    }

    private void DeleteReplayDraft(string gameId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Duel replay draft delete is skipped on WebGL platform.", ("gameId", gameId));
#else
        if (string.IsNullOrEmpty(gameId)) {
            return;
        }

        string gamePath = GameSaveConfig.GetReplayGamePath(gameId);
        try {
            if (Directory.Exists(gamePath)) {
                Directory.Delete(gamePath, true);
            }
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel replay draft delete failed.", ("gameId", gameId), ("err", ex.Message));
        }
#endif
    }
}
