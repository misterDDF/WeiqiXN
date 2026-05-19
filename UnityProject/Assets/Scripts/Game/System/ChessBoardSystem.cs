using System;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public class ChessBoardSystem : SystemBase
{
    public override string systemName => GetSystemName<ChessBoardSystem>();
    public ChessBoardDataType chessBoardData;

    public ChessBoardSystem(SceneBase scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();

        scene.RegisterEntityEvent<Chess, OnEntityCreated>(OnChessCreated);
        scene.RegisterSystemEvent<OnAddChessToBoard>(OnAddChessToBoard);

        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard == null) return;

        // 非读档进来的需要手动初始化
        if (scene.sceneCreateParams.saveFilePath == null) {
            if (scene.sceneCreateParams.duelSceneCreateParamas != null) {
                compChessBoard.boardCfgId.value = scene.sceneCreateParams.duelSceneCreateParamas.boardCfgId;
            } else {
                XNLogger.LogError("Scene create params for duel scene is empty, init scene with default values.");
                compChessBoard.boardCfgId.value = "9x9";
            }
        }
        chessBoardData = ChessBoardDataType.GetConfigData(compChessBoard.boardCfgId.value);
        if (chessBoardData != null) {
            compChessBoard.chessBoardGrid.InitGrid(chessBoardData.boardSize);
            int chessBoardCellCount = compChessBoard.chessBoardGrid.gridSize * compChessBoard.chessBoardGrid.gridSize;
            Bounds gridBounds = compChessBoard.chessBoardGrid.GetGridBounds();
            InitDuelVCam(gridBounds);
        } else {
            XNLogger.LogError("Chess board config not found!", ("chessBoardCfgId", compChessBoard.boardCfgId.value));
        }

        // 读档进来的还原棋子entity
        if (scene.sceneCreateParams.saveFilePath != null) {
            RestoreBoardFromKataGoRecord(compChessBoard);
        }
    }

    public void OnChessCreated(Chess chess, OnEntityCreated evt)
    {
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard != null) {
            int posIndex = compChessBoard.GetPosIndexByCoords(chess.coords);
            if (posIndex >= 0) {
                if (compChessBoard.chessInfoDict.TryGetValue(posIndex.ToString(), out var chessInfo) &&
                    chessInfo.chessGuid.value == chess.guid &&
                    chessInfo.chessFlag.value == (int)chess.playerFlag
                ) {
                    Transform gridTransform = compChessBoard.chessBoardGrid.transform;
                    Vector3 localChessPos = compChessBoard.chessBoardGrid.GetCellCenterLocalPosition(chess.coords.x, chess.coords.z);
                    chess.transform.position = gridTransform.TransformPoint(localChessPos);
                } else {
                    chess.Destroy();
                }
            }
        }
    }

    public void OnAddChessToBoard(OnAddChessToBoard evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel != null && compChessBoard != null) {
            int posIndex = compChessBoard.GetPosIndexByCoords(evt.coords);
            var curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
            if (posIndex >= 0 && !compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString()) && curPlayer != null) {
                string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
                var cachedChessInfoDict = compChessBoard.CreateCacheChessInfoDict();

                ChessInfo addChessInfo = new ChessInfo();
                addChessInfo.chessGuid.value = chessGuid;
                addChessInfo.chessFlag.value = curPlayer.playerFlag.value;
                compChessBoard.chessInfoDict.SetValue(posIndex.ToString(), addChessInfo);
                List<int> pendingRemovePosIndexes = GetPendingRemovePosIndexes((PlayerFlag)curPlayer.playerFlag.value, evt.coords);
                foreach (var removePosIndex in pendingRemovePosIndexes) {
                    compChessBoard.chessInfoDict.Remove(removePosIndex.ToString());
                }

                // 落子提子后，还会导致自杀是非法的
                List<int> selfRemovePosIndexes = GetPendingRemovePosIndexes(((PlayerFlag)curPlayer.playerFlag.value).GetOpponentPlayerFlag(), evt.coords);
                if (selfRemovePosIndexes.Count > 0) {
                    // TODO show message
                    compChessBoard.chessInfoDict = cachedChessInfoDict;
                    return;
                }
                // 额外检查单独落子是否自杀
                if (!CheckSingleChessValid((PlayerFlag)curPlayer.playerFlag.value, evt.coords)) {
                    // TODO show message
                    compChessBoard.chessInfoDict = cachedChessInfoDict;
                    return;
                }
                // 落子前后棋盘状态不能完全一致，防止打劫
                if (compChessBoard.CheckChessFlagChanged()) {
                    foreach (var removePosIndex in pendingRemovePosIndexes) {
                        // 注意chessInfoMap提子的guid已经清理了，这里要从上次的状态去找
                        if (cachedChessInfoDict.TryGetValue(removePosIndex.ToString(), out var _chessInfo)) {
                            var chess = scene.GetEntity<Chess>(_chessInfo.chessGuid.value);
                            if (chess != null) {
                                chess.Destroy();
                            }
                        }
                    }
                    compChessBoard.lastChessInfoDict = cachedChessInfoDict;
                    EntityUtils.CreateChess(scene, chessGuid, (PlayerFlag)curPlayer.playerFlag.value, evt.coords);
                    int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : chessBoardData?.boardSize ?? 19;
                    compDuel.AppendKataGoMove((PlayerFlag)curPlayer.playerFlag.value, evt.coords, boardSize);
                    scene.EmitSystemEvent(new OnAfterAddChessToBoard((PlayerFlag)curPlayer.playerFlag.value, evt.coords.Clone()));
                } else {
                    // TODO show message
                    compChessBoard.chessInfoDict = cachedChessInfoDict;
                    return;
                }
            }
        }
    }

    private void InitDuelVCam(Bounds gridBound)
    {
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard.duelVCam == null) {
            XNLogger.LogError("Duel virtual camera not found, init camera failed.");
            return;
        }

        Transform duelVCamTransform = compChessBoard.duelVCam.transform;

        // 让相机始终垂直朝向 y 轴负方向，形成俯视棋盘的视角
        duelVCamTransform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        float aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;

        // 以棋盘中心为基准，只沿 y 轴正方向抬升相机位置
        float extraYOffset = 0;
        if (chessBoardData != null) {
            extraYOffset = chessBoardData.vcamYOffset;
        }

        LensSettings lens = compChessBoard.duelVCam.m_Lens;
        float halfVerticalFovRad = lens.FieldOfView * 0.5f * Mathf.Deg2Rad;
        float halfHeightByBoard = Mathf.Max(gridBound.extents.z, aspect > 0f ? gridBound.extents.x / aspect : gridBound.extents.z);
        float halfHeightPadding = Mathf.Tan(halfVerticalFovRad) * extraYOffset;
        lens.OrthographicSize = halfHeightByBoard + Mathf.Max(halfHeightPadding, 0f);
        lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
        compChessBoard.duelVCam.m_Lens = lens;

        float cameraYOffset = Mathf.Max(gridBound.size.x, gridBound.size.z) + extraYOffset;
        duelVCamTransform.position = gridBound.center + Vector3.up * cameraYOffset;
    }

    private void RestoreBoardFromKataGoRecord(SceneComponentChessBoard compChessBoard)
    {
        string recordFilePath = GameSaveConfig.GetDuelRecordSavePath(0);
        if (!KataGoDuelRecordFile.TryLoad(recordFilePath, out var recordJson) ||
            !KataGoDuelRecordFile.TryGetMoves(recordJson, out var moves)) {
            XNLogger.LogError("Restore board from KataGo record failed.", ("recordFilePath", recordFilePath));
            return;
        }

        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            XNLogger.LogError("Restore board from KataGo record failed, duel component not found.");
            return;
        }

        compChessBoard.chessInfoDict.Clear();
        compChessBoard.lastChessInfoDict.Clear();
        compDuel.ResetKataGoMoves();

        int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : chessBoardData?.boardSize ?? 19;
        if (!KataGoDuelRecordFile.TryGetBoardSize(recordJson, out int recordBoardSize)) {
            XNLogger.LogError("KataGo duel record board size invalid, restore skipped.", ("recordFilePath", recordFilePath));
            return;
        }

        if (recordBoardSize != boardSize) {
            XNLogger.LogError(
                "KataGo duel record board size mismatch, restore skipped.",
                ("recordBoardSize", recordBoardSize.ToString()),
                ("boardSize", boardSize.ToString()));
            return;
        }

        foreach (var move in moves) {
            if (!KataGoDuelRecordFile.TryParseMove(move, out PlayerFlag playerFlag, out RectCoordinates coords, boardSize)) {
                XNLogger.LogError("Invalid move in KataGo duel record, restore stopped.", ("move", move.ToString()));
                compChessBoard.chessInfoDict.Clear();
                compChessBoard.lastChessInfoDict.Clear();
                compDuel.ResetKataGoMoves();
                return;
            }

            if (!ApplyRecordMove(compChessBoard, compDuel, playerFlag, coords, boardSize)) {
                XNLogger.LogError("Invalid move in KataGo duel record, restore stopped.", ("move", move.ToString()));
                compChessBoard.chessInfoDict.Clear();
                compChessBoard.lastChessInfoDict.Clear();
                compDuel.ResetKataGoMoves();
                return;
            }
        }

        foreach (var kvp in compChessBoard.chessInfoDict) {
            if (!int.TryParse(kvp.Key, out int posIndex)) {
                continue;
            }

            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(posIndex);
            ChessInfo chessInfo = kvp.Value;
            if (coords.x >= 0 && coords.z >= 0 && chessInfo != null) {
                EntityUtils.CreateChess(scene, chessInfo.chessGuid.value, (PlayerFlag)chessInfo.chessFlag.value, coords);
            }
        }
    }

    private bool ApplyRecordMove(SceneComponentChessBoard compChessBoard, SceneComponentDuel compDuel, PlayerFlag playerFlag, RectCoordinates coords, int boardSize)
    {
        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            XNLogger.LogError("Invalid record move position, restore move skipped.", ("coords", coords?.ToString() ?? "null"));
            return false;
        }

        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        var cachedChessInfoDict = compChessBoard.CreateCacheChessInfoDict();

        ChessInfo addChessInfo = new ChessInfo();
        addChessInfo.chessGuid.value = chessGuid;
        addChessInfo.chessFlag.value = (int)playerFlag;

        compChessBoard.chessInfoDict.SetValue(posIndex.ToString(), addChessInfo);
        List<int> pendingRemovePosIndexes = GetPendingRemovePosIndexes(playerFlag, coords);
        foreach (int removePosIndex in pendingRemovePosIndexes) {
            compChessBoard.chessInfoDict.Remove(removePosIndex.ToString());
        }

        List<int> selfRemovePosIndexes = GetPendingRemovePosIndexes(playerFlag.GetOpponentPlayerFlag(), coords);
        if (selfRemovePosIndexes.Count > 0 || !CheckSingleChessValid(playerFlag, coords) || !compChessBoard.CheckChessFlagChanged()) {
            compChessBoard.chessInfoDict = cachedChessInfoDict;
            return false;
        }

        compChessBoard.lastChessInfoDict = cachedChessInfoDict;
        compDuel.AppendKataGoMove(playerFlag, coords, boardSize);
        return true;
    }

    private static int[] dirX = { 0, 0, 1, -1 };
    private static int[] dirZ = { 1, -1, 0, 0 };
    private bool[] visited;
    // 新增棋子时，BFS遍历失去所有气的棋子串
    private List<int> GetPendingRemovePosIndexes(PlayerFlag playerFlag, RectCoordinates coords)
    {
        List<int> pendingRemovePosIndexes = new List<int>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard != null) {
            visited = new bool[compChessBoard.GetGridMaxSize()];
            for (int dir = 0; dir < Math.Min(dirX.Length, dirZ.Length); dir++) {
                int nx = coords.x + dirX[dir];
                int nz = coords.z + dirZ[dir];
                int nPosIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));

                if (nPosIndex < 0 || visited[nPosIndex]) {
                    continue;
                }
                List<int> connectGroup = GetConnectGroup(nPosIndex, playerFlag.GetOpponentPlayerFlag());
                if (!CheckGroupHasLiberty(connectGroup)) {
                    foreach (int _posIndex in connectGroup) {
                        pendingRemovePosIndexes.Add(_posIndex);
                    }
                }
            }
        }
        return pendingRemovePosIndexes;
    }

    // 计算连通图
    private List<int> GetConnectGroup(int startIndex, PlayerFlag targetPlayerFlag)
    {
        List<int> connectGroup = new List<int>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard != null) {
            if (startIndex < 0 || startIndex >= compChessBoard.GetGridMaxSize()) {
                return connectGroup;
            }

            if (!compChessBoard.chessInfoDict.TryGetValue(startIndex.ToString(), out var startChessInfo)) {
                return connectGroup;
            }

            // 首先要求起点必须同色
            if (startChessInfo == null || startChessInfo.chessFlag.value != (int)targetPlayerFlag) {
                return connectGroup;
            }

            int gridSize = compChessBoard.chessBoardGrid.gridSize;
            Queue<int> bfsQueue = new Queue<int>();

            bfsQueue.Enqueue(startIndex);
            visited[startIndex] = true;
            connectGroup.Add(startIndex);

            while (bfsQueue.Count > 0) {
                int curIndex = bfsQueue.Dequeue();
                int curX = curIndex % gridSize;
                int curZ = curIndex / gridSize;

                for (int dir = 0; dir < Math.Min(dirX.Length, dirZ.Length); dir++) {
                    int nx = curX + dirX[dir];
                    int nz = curZ + dirZ[dir];
                    int nextIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));
                    if (nextIndex < 0 || visited[nextIndex]) {
                        continue;
                    }

                    if (compChessBoard.chessInfoDict.TryGetValue(nextIndex.ToString(), out var nextChessInfo)) {
                        if (nextChessInfo.chessFlag.value != (int)targetPlayerFlag) {
                            continue;
                        }
                    } else {
                        continue;
                    }

                    bfsQueue.Enqueue(nextIndex);
                    visited[nextIndex] = true;
                    connectGroup.Add(nextIndex);
                }
            }
        }

        return connectGroup;
    }

    // 检查连通图是否有气
    private bool CheckGroupHasLiberty(List<int> connectGroup)
    {
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard != null) {
            int gridSize = compChessBoard.chessBoardGrid.gridSize;
            foreach (int posIndex in connectGroup) {
                if (posIndex < 0 || posIndex >= compChessBoard.GetGridMaxSize()) {
                    continue;
                }

                int curX = posIndex % gridSize;
                int curZ = posIndex / gridSize;
                for (int dir = 0; dir < Math.Min(dirX.Length, dirZ.Length); dir++) {
                    int nx = curX + dirX[dir];
                    int nz = curZ + dirZ[dir];
                    int neighborIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));
                    if (neighborIndex < 0) {
                        continue;
                    }

                    if (!compChessBoard.chessInfoDict.ContainsKey(neighborIndex.ToString())) {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // 检查单一落子是否为死子
    private bool CheckSingleChessValid(PlayerFlag playerFlag, RectCoordinates coords)
    {
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard != null) {
            for (int dir = 0; dir < Math.Min(dirX.Length, dirZ.Length); dir++) {
                int nx = coords.x + dirX[dir];
                int nz = coords.z + dirZ[dir];
                int neighborIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));
                if (neighborIndex < 0) {
                    continue;
                }

                if (compChessBoard.chessInfoDict.TryGetValue(neighborIndex.ToString(), out var neighborChessInfo)) {
                    if (neighborChessInfo.chessFlag.value == (int)playerFlag) {
                        return true;
                    }
                } else {
                    return true;
                }
            }
        }

        return false;
    }
}
