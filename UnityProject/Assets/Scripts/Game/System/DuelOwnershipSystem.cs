using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public class DuelOwnershipSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelOwnershipSystem>();

    private const float OwnershipThreshold = 0.2f;

    private bool isAnalyzing;
    private int requestVersion;

    public DuelOwnershipSystem(DuelScene scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnRequestDuelOwnership>(OnRequestDuelOwnership);
        scene.RegisterSystemEvent<OnRequestClearDuelOwnership>(OnRequestClearDuelOwnership);
        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnRequestDuelPass>(OnRequestDuelPass);
    }

    private void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        ClearOwnershipAndNotify();
    }

    private void OnRequestClearDuelOwnership(OnRequestClearDuelOwnership evt)
    {
        ClearOwnershipAndNotify();
    }

    private void OnRequestDuelPass(OnRequestDuelPass evt)
    {
        ClearOwnershipAndNotify();
    }

    private async void OnRequestDuelOwnership(OnRequestDuelOwnership evt)
    {
        if (isAnalyzing) {
            XNLogger.LogWarn("Duel ownership analyze skipped, request is already running.");
            return;
        }

        DuelScene duelScene = scene as DuelScene;
        if (duelScene == null) {
            XNLogger.LogError("Duel ownership analyze failed, scene is not DuelScene.");
            return;
        }

        isAnalyzing = true;
        try {
            requestVersion += 1;
            int currentRequestVersion = requestVersion;
            ClearOwnershipOverlay();

            string requestId = $"duel-ownership-{DateTime.UtcNow.Ticks}";
            JObject query = KataGoPositionJsonBuilder.BuildOwnershipAnalysisJson(duelScene, requestId);
            JArray ownership = await KataGoBootstrap.AnalyzeOwnershipAsync(query);
            if (currentRequestVersion != requestVersion) {
                return;
            }

            if (ownership == null) {
                ClearOwnershipAndNotify();
                return;
            }

            SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
            if (compChessBoard?.chessBoardGrid == null) {
                XNLogger.LogError("Duel ownership draw failed, chess board grid is missing.");
                return;
            }

            compChessBoard.chessBoardGrid.DrawOwnership(ownership, OwnershipThreshold);
            OwnershipScore score = CalculateOwnershipScore(ownership, query);
            scene.EmitSystemEvent(new OnDuelOwnershipResult(score.blackPoints, score.whitePoints, score.komi));
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel ownership analyze failed.", ("err", ex.Message));
            ClearOwnershipAndNotify();
        }
        finally {
            isAnalyzing = false;
        }
    }

    private void ClearOwnershipAndNotify()
    {
        requestVersion += 1;
        ClearOwnershipOverlay();
        scene.EmitSystemEvent(new OnClearDuelOwnership());
    }

    private void ClearOwnershipOverlay()
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        compChessBoard?.chessBoardGrid?.ClearOwnership();
    }

    private OwnershipScore CalculateOwnershipScore(JArray ownership, JObject query)
    {
        float blackPoints = 0f;
        float whitePoints = 0f;
        foreach (JToken ownershipToken in ownership) {
            if (!float.TryParse(ownershipToken?.ToString(), out float ownershipValue)) {
                continue;
            }

            if (ownershipValue > OwnershipThreshold) {
                blackPoints += 1f;
            } else if (ownershipValue < -OwnershipThreshold) {
                whitePoints += 1f;
            }
        }

        float komi = 0f;
        float.TryParse(query?["komi"]?.ToString(), out komi);
        whitePoints += komi;

        return new OwnershipScore
        {
            blackPoints = blackPoints,
            whitePoints = whitePoints,
            komi = komi,
        };
    }

    private struct OwnershipScore
    {
        public float blackPoints;
        public float whitePoints;
        public float komi;
    }
}
