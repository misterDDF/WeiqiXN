using System;
using System.Collections.Generic;
using XNClient.ChessBoard;

public static class DuelMoveRule
{
    private static readonly int[] DirX = { 0, 0, 1, -1 };
    private static readonly int[] DirZ = { 1, -1, 0, 0 };

    public static bool CheckMoveLegal(SceneComponentChessBoard compChessBoard, PlayerFlag playerFlag, RectCoordinates coords)
    {
        bool legal = TryApplyMove(compChessBoard, playerFlag, coords, string.Empty, out SavableObjectDict<ChessInfo> previousChessInfoDict, out _);
        if (legal) {
            compChessBoard.chessInfoDict = previousChessInfoDict;
        }

        return legal;
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
        if (compChessBoard == null || coords == null) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            return false;
        }

        previousChessInfoDict = compChessBoard.CreateCacheChessInfoDict();

        ChessInfo addChessInfo = new ChessInfo();
        addChessInfo.chessGuid.value = chessGuid;
        addChessInfo.chessFlag.value = (int)playerFlag;
        compChessBoard.chessInfoDict.SetValue(posIndex.ToString(), addChessInfo);

        pendingRemovePosIndexes = GetPendingRemovePosIndexes(compChessBoard, playerFlag, coords);
        foreach (int removePosIndex in pendingRemovePosIndexes) {
            compChessBoard.chessInfoDict.Remove(removePosIndex.ToString());
        }

        List<int> selfRemovePosIndexes = GetPendingRemovePosIndexes(compChessBoard, playerFlag.GetOpponentPlayerFlag(), coords);
        bool legal = selfRemovePosIndexes.Count == 0
            && CheckSingleChessValid(compChessBoard, playerFlag, coords)
            && compChessBoard.CheckChessFlagChanged();

        if (!legal) {
            compChessBoard.chessInfoDict = previousChessInfoDict;
        }

        return legal;
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
