using System;
using System.Collections.Generic;
using XNClient.ChessBoard;

public enum DuelMoveRejectReason
{
    None,
    InvalidBoard,
    InvalidCommand,
    OutOfBoard,
    PositionOccupied,
    Suicide,
    RepeatedBoard,
    NotPlayerTurn,
    BoardVersionMismatch,
}

public class DuelMoveCommand
{
    public PlayerFlag playerFlag;
    public RectCoordinates coords;
    public string chessGuid;

    public DuelMoveCommand(PlayerFlag playerFlag, RectCoordinates coords, string chessGuid)
    {
        this.playerFlag = playerFlag;
        this.coords = coords;
        this.chessGuid = chessGuid ?? string.Empty;
    }
}

public class DuelMoveResult
{
    public bool accepted;
    public DuelMoveRejectReason rejectReason;
    public PlayerFlag playerFlag;
    public RectCoordinates coords;
    public string chessGuid;
    public int posIndex = -1;
    public SavableObjectDict<ChessInfo> previousChessInfoDict;
    public SavableObjectDict<ChessInfo> nextChessInfoDict;
    public List<int> pendingRemovePosIndexes = new List<int>();
}

public static class DuelMoveRule
{
    private static readonly int[] DirX = { 0, 0, 1, -1 };
    private static readonly int[] DirZ = { 1, -1, 0, 0 };

    public static bool CheckMoveLegal(SceneComponentChessBoard compChessBoard, PlayerFlag playerFlag, RectCoordinates coords)
    {
        return BuildMoveResult(compChessBoard, new DuelMoveCommand(playerFlag, coords, string.Empty)).accepted;
    }

    public static bool TryApplyMove(
        SceneComponentChessBoard compChessBoard,
        PlayerFlag playerFlag,
        RectCoordinates coords,
        string chessGuid,
        out SavableObjectDict<ChessInfo> previousChessInfoDict,
        out List<int> pendingRemovePosIndexes
    )
    {
        previousChessInfoDict = null;
        pendingRemovePosIndexes = new List<int>();
        DuelMoveResult result = BuildMoveResult(compChessBoard, new DuelMoveCommand(playerFlag, coords, chessGuid));
        if (!result.accepted) {
            return false;
        }

        ApplyMoveResult(compChessBoard, result);
        previousChessInfoDict = result.previousChessInfoDict;
        pendingRemovePosIndexes = result.pendingRemovePosIndexes;
        return true;
    }

    public static bool TryBuildMoveResult(SceneComponentChessBoard compChessBoard, DuelMoveCommand command, out DuelMoveResult result)
    {
        result = BuildMoveResult(compChessBoard, command);
        return true;
    }

    public static DuelMoveResult BuildMoveResult(SceneComponentChessBoard compChessBoard, DuelMoveCommand command)
    {
        DuelMoveResult result = new DuelMoveResult();
        if (command != null) {
            result.playerFlag = command.playerFlag;
            result.coords = command.coords?.Clone();
            result.chessGuid = command.chessGuid ?? string.Empty;
        }

        if (compChessBoard == null || compChessBoard.chessBoardGrid == null || compChessBoard.chessInfoDict == null || compChessBoard.lastChessInfoDict == null) {
            result.rejectReason = DuelMoveRejectReason.InvalidBoard;
            return result;
        }

        if (command == null || command.coords == null || command.playerFlag == 0) {
            result.rejectReason = DuelMoveRejectReason.InvalidCommand;
            return result;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(command.coords);
        result.posIndex = posIndex;
        if (posIndex < 0) {
            result.rejectReason = DuelMoveRejectReason.OutOfBoard;
            return result;
        }

        if (compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            result.rejectReason = DuelMoveRejectReason.PositionOccupied;
            return result;
        }

        SavableObjectDict<ChessInfo> originalChessInfoDict = compChessBoard.chessInfoDict;
        SavableObjectDict<ChessInfo> previousChessInfoDict = compChessBoard.CreateCacheChessInfoDict();
        SavableObjectDict<ChessInfo> workingChessInfoDict = CloneChessInfoDict(previousChessInfoDict);
        compChessBoard.chessInfoDict = workingChessInfoDict;
        try {
            ChessInfo addChessInfo = new ChessInfo();
            addChessInfo.chessGuid.value = command.chessGuid ?? string.Empty;
            addChessInfo.chessFlag.value = (int)command.playerFlag;
            compChessBoard.chessInfoDict.SetValue(posIndex.ToString(), addChessInfo);

            List<int> pendingRemovePosIndexes = GetPendingRemovePosIndexes(compChessBoard, command.playerFlag, command.coords);
            foreach (int removePosIndex in pendingRemovePosIndexes) {
                compChessBoard.chessInfoDict.Remove(removePosIndex.ToString());
            }

            List<int> selfRemovePosIndexes = GetPendingRemovePosIndexes(compChessBoard, command.playerFlag.GetOpponentPlayerFlag(), command.coords);
            if (selfRemovePosIndexes.Count > 0 || !CheckSingleChessValid(compChessBoard, command.playerFlag, command.coords)) {
                result.rejectReason = DuelMoveRejectReason.Suicide;
                result.previousChessInfoDict = previousChessInfoDict;
                result.pendingRemovePosIndexes = pendingRemovePosIndexes;
                return result;
            }

            if (!compChessBoard.CheckChessFlagChanged()) {
                result.rejectReason = DuelMoveRejectReason.RepeatedBoard;
                result.previousChessInfoDict = previousChessInfoDict;
                result.pendingRemovePosIndexes = pendingRemovePosIndexes;
                return result;
            }

            result.accepted = true;
            result.rejectReason = DuelMoveRejectReason.None;
            result.previousChessInfoDict = previousChessInfoDict;
            result.nextChessInfoDict = compChessBoard.CreateCacheChessInfoDict();
            result.pendingRemovePosIndexes = pendingRemovePosIndexes;
            return result;
        }
        finally {
            compChessBoard.chessInfoDict = originalChessInfoDict;
        }
    }

    public static void ApplyMoveResult(SceneComponentChessBoard compChessBoard, DuelMoveResult result)
    {
        if (compChessBoard == null || result == null || !result.accepted || result.nextChessInfoDict == null || result.previousChessInfoDict == null) {
            return;
        }

        compChessBoard.chessInfoDict = CloneChessInfoDict(result.nextChessInfoDict);
        compChessBoard.lastChessInfoDict = CloneChessInfoDict(result.previousChessInfoDict);
    }

    private static SavableObjectDict<ChessInfo> CloneChessInfoDict(SavableObjectDict<ChessInfo> source)
    {
        SavableObjectDict<ChessInfo> cloned = new SavableObjectDict<ChessInfo>();
        if (source == null) {
            return cloned;
        }

        foreach (var kvp in source) {
            if (kvp.Value == null) {
                continue;
            }

            ChessInfo chessInfo = new ChessInfo();
            chessInfo.chessGuid.value = kvp.Value.chessGuid.value;
            chessInfo.chessFlag.value = kvp.Value.chessFlag.value;
            cloned.SetValue(kvp.Key, chessInfo);
        }
        return cloned;
    }

    private static List<int> GetPendingRemovePosIndexes(SceneComponentChessBoard compChessBoard, PlayerFlag playerFlag, RectCoordinates coords)
    {
        List<int> pendingRemovePosIndexes = new List<int>();
        if (compChessBoard == null) {
            return pendingRemovePosIndexes;
        }

        bool[] visited = new bool[compChessBoard.GetGridMaxSize()];
        for (int dir = 0; dir < Math.Min(DirX.Length, DirZ.Length); dir++) {
            int nx = coords.x + DirX[dir];
            int nz = coords.z + DirZ[dir];
            int nPosIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));

            if (nPosIndex < 0 || visited[nPosIndex]) {
                continue;
            }

            List<int> connectGroup = GetConnectGroup(compChessBoard, nPosIndex, playerFlag.GetOpponentPlayerFlag(), visited);
            if (!CheckGroupHasLiberty(compChessBoard, connectGroup)) {
                foreach (int posIndex in connectGroup) {
                    pendingRemovePosIndexes.Add(posIndex);
                }
            }
        }

        return pendingRemovePosIndexes;
    }

    private static List<int> GetConnectGroup(
        SceneComponentChessBoard compChessBoard,
        int startIndex,
        PlayerFlag targetPlayerFlag,
        bool[] visited
    )
    {
        List<int> connectGroup = new List<int>();
        if (compChessBoard == null || startIndex < 0 || startIndex >= compChessBoard.GetGridMaxSize()) {
            return connectGroup;
        }

        if (!compChessBoard.chessInfoDict.TryGetValue(startIndex.ToString(), out ChessInfo startChessInfo)) {
            return connectGroup;
        }

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

            for (int dir = 0; dir < Math.Min(DirX.Length, DirZ.Length); dir++) {
                int nx = curX + DirX[dir];
                int nz = curZ + DirZ[dir];
                int nextIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));
                if (nextIndex < 0 || visited[nextIndex]) {
                    continue;
                }

                if (compChessBoard.chessInfoDict.TryGetValue(nextIndex.ToString(), out ChessInfo nextChessInfo)) {
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

        return connectGroup;
    }

    private static bool CheckGroupHasLiberty(SceneComponentChessBoard compChessBoard, List<int> connectGroup)
    {
        if (compChessBoard == null) {
            return false;
        }

        int gridSize = compChessBoard.chessBoardGrid.gridSize;
        foreach (int posIndex in connectGroup) {
            if (posIndex < 0 || posIndex >= compChessBoard.GetGridMaxSize()) {
                continue;
            }

            int curX = posIndex % gridSize;
            int curZ = posIndex / gridSize;
            for (int dir = 0; dir < Math.Min(DirX.Length, DirZ.Length); dir++) {
                int nx = curX + DirX[dir];
                int nz = curZ + DirZ[dir];
                int neighborIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));
                if (neighborIndex < 0) {
                    continue;
                }

                if (!compChessBoard.chessInfoDict.ContainsKey(neighborIndex.ToString())) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CheckSingleChessValid(SceneComponentChessBoard compChessBoard, PlayerFlag playerFlag, RectCoordinates coords)
    {
        if (compChessBoard == null) {
            return false;
        }

        for (int dir = 0; dir < Math.Min(DirX.Length, DirZ.Length); dir++) {
            int nx = coords.x + DirX[dir];
            int nz = coords.z + DirZ[dir];
            int neighborIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));
            if (neighborIndex < 0) {
                continue;
            }

            if (compChessBoard.chessInfoDict.TryGetValue(neighborIndex.ToString(), out ChessInfo neighborChessInfo)) {
                if (neighborChessInfo.chessFlag.value == (int)playerFlag) {
                    return true;
                }
            } else {
                return true;
            }
        }

        return false;
    }
}
