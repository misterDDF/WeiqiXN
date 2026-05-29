using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.ChessBoard;

public static class KataGoAiAnalysisRenderService
{
    public static bool DrawOwnership(SceneComponentChessBoard compChessBoard, JObject result)
    {
        JArray ownership = result?["ownership"] as JArray;
        if (ownership == null || compChessBoard?.chessBoardGrid == null) {
            return false;
        }

        if (!HasVisibleOwnership(ownership, compChessBoard.chessBoardGrid.gridSize, DuelOwnershipQueryService.OwnershipThreshold)) {
            return false;
        }

        compChessBoard.chessBoardGrid.DrawOwnership(ownership, DuelOwnershipQueryService.OwnershipThreshold);
        return true;
    }

    public static bool HasVisibleOwnership(JArray ownership, int boardSize, float ownershipThreshold)
    {
        int expectedCount = boardSize * boardSize;
        if (ownership == null || ownership.Count < expectedCount) {
            return false;
        }

        for (int i = 0; i < expectedCount; i++) {
            if (float.TryParse(ownership[i]?.ToString(), out float ownershipValue) &&
                Mathf.Abs(ownershipValue) > ownershipThreshold) {
                return true;
            }
        }

        return false;
    }

    public static List<RectGridAiRecommendationMarker> BuildRecommendationMarkers(
        SceneBase scene,
        JObject result,
        PlayerFlag currentPlayerFlag,
        Dictionary<int, List<ReplayAiVariationMove>> variations)
    {
        List<RectGridAiRecommendationMarker> markers = new List<RectGridAiRecommendationMarker>();
        SceneComponentChessBoard compChessBoard = scene?.GetComponent<SceneComponentChessBoard>();
        JArray moveInfos = result?["moveInfos"] as JArray;
        if (moveInfos == null || moveInfos.Count == 0 || compChessBoard?.chessBoardGrid == null) {
            return markers;
        }

        int boardSize = compChessBoard.chessBoardGrid.gridSize;
        int displayLimit = KataGoAiAnalysisConfigService.DisplayCandidateLimit;
        int requestLimit = KataGoAiAnalysisConfigService.RequestCandidateLimit;
        bool showCurrentPlayerWinrate = KataGoAiAnalysisConfigService.ShowCurrentPlayerWinrate;
        int winrateMinDisplay = KataGoAiAnalysisConfigService.WinrateMinDisplay;
        int winrateMaxDisplay = KataGoAiAnalysisConfigService.WinrateMaxDisplay;
        List<JToken> sortedMoveInfos = BuildSortedMoveInfos(moveInfos);
        int parsedCount = 0;

        foreach (JToken token in sortedMoveInfos) {
            if (markers.Count >= displayLimit || parsedCount >= requestLimit) {
                break;
            }

            parsedCount += 1;
            string move = token?["move"]?.ToString();
            if (string.Equals(move, KataGoPositionJsonBuilder.PassPoint, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(move, boardSize, out RectCoordinates coords) ||
                !TryParseFloat(token?["winrate"], out float winrate)) {
                continue;
            }

            if (!IsLegalRecommendation(compChessBoard, coords, currentPlayerFlag)) {
                continue;
            }

            if (showCurrentPlayerWinrate && currentPlayerFlag == PlayerFlag.Player2) {
                winrate = 1f - winrate;
            }

            int winratePercent = Mathf.RoundToInt(Mathf.Clamp01(winrate) * 100f);
            winratePercent = Mathf.Clamp(winratePercent, winrateMinDisplay, winrateMaxDisplay);
            markers.Add(new RectGridAiRecommendationMarker(coords.x, coords.z, winratePercent, markers.Count + 1));

            if (variations != null) {
                int posIndex = compChessBoard.GetPosIndexByCoords(coords);
                List<ReplayAiVariationMove> variation = BuildRecommendationVariation(token, coords, currentPlayerFlag, boardSize);
                if (posIndex >= 0 && variation.Count > 0) {
                    variations[posIndex] = variation;
                }
            }
        }

        return markers;
    }

    private static List<ReplayAiVariationMove> BuildRecommendationVariation(JToken moveInfo, RectCoordinates recommendationCoords, PlayerFlag firstPlayerFlag, int boardSize)
    {
        List<ReplayAiVariationMove> variation = new List<ReplayAiVariationMove>();
        JArray pv = moveInfo?["pv"] as JArray;
        if (pv == null || pv.Count <= 1 || recommendationCoords == null || firstPlayerFlag == 0) {
            return variation;
        }

        PlayerFlag playerFlag = firstPlayerFlag;
        for (int i = 0; i < pv.Count; i++) {
            string point = pv[i]?.ToString();
            if (string.Equals(point, KataGoPositionJsonBuilder.PassPoint, StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(point, boardSize, out RectCoordinates coords)) {
                break;
            }

            if (i == 0) {
                if (!IsSameCoords(coords, recommendationCoords)) {
                    variation.Clear();
                    return variation;
                }

                playerFlag = playerFlag.GetOpponentPlayerFlag();
                continue;
            }

            variation.Add(new ReplayAiVariationMove
            {
                playerFlag = playerFlag,
                coords = coords.Clone(),
            });
            playerFlag = playerFlag.GetOpponentPlayerFlag();
        }

        return variation;
    }

    private static List<JToken> BuildSortedMoveInfos(JArray moveInfos)
    {
        List<JToken> sorted = new List<JToken>();
        foreach (JToken token in moveInfos) {
            sorted.Add(token);
        }

        sorted.Sort((left, right) => GetMoveInfoOrder(left).CompareTo(GetMoveInfoOrder(right)));
        return sorted;
    }

    private static int GetMoveInfoOrder(JToken token)
    {
        return int.TryParse(token?["order"]?.ToString(), out int order) ? order : int.MaxValue;
    }

    private static bool IsLegalRecommendation(SceneComponentChessBoard compChessBoard, RectCoordinates coords, PlayerFlag playerFlag)
    {
        if (coords == null || playerFlag == 0 || compChessBoard == null) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            return false;
        }

        return DuelMoveRule.CheckMoveLegal(compChessBoard, playerFlag, coords);
    }

    private static bool IsSameCoords(RectCoordinates left, RectCoordinates right)
    {
        return left != null && right != null && left.x == right.x && left.z == right.z;
    }

    private static bool TryParseFloat(JToken token, out float value)
    {
        return float.TryParse(token?.ToString(), out value);
    }
}
