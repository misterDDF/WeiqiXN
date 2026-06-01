using UnityEngine;

public class DuelGameEndCameraSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelGameEndCameraSystem>();

    private const float GameEndCameraDistanceFactor = 1.35f;
    private const float GameEndCameraTransitionSeconds = 1.5f;
    private const float RestoreCameraTransitionSeconds = 0.35f;

    private bool hasNormalCameraPosition;
    private bool isAnimating;
    private bool isGameEndCameraActive;
    private Vector3 normalCameraPosition;
    private Vector3 animationStartPosition;
    private Vector3 animationTargetPosition;
    private float animationElapsedSeconds;
    private float animationDurationSeconds;

    public DuelGameEndCameraSystem(DuelScene scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        CaptureNormalCameraPosition();
        scene.RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
        UpdateCameraAnimation();
    }

    private void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        if (evt == null) {
            return;
        }

        if (evt.curStateName == DuelStateDefine.STATE_GAME_END) {
            StartGameEndCameraAnimation();
            return;
        }

        if (evt.curStateName == DuelStateDefine.STATE_TURN_INPUT) {
            StartRestoreCameraAnimation();
        }
    }

    private void CaptureNormalCameraPosition()
    {
        Transform cameraTransform = GetDuelCameraTransform();
        if (cameraTransform == null) {
            hasNormalCameraPosition = false;
            return;
        }

        normalCameraPosition = cameraTransform.position;
        hasNormalCameraPosition = true;
    }

    private void StartGameEndCameraAnimation()
    {
        if (!hasNormalCameraPosition) {
            CaptureNormalCameraPosition();
        }

        Transform cameraTransform = GetDuelCameraTransform();
        Bounds? gridBounds = GetGridBounds();
        if (cameraTransform == null || !hasNormalCameraPosition || !gridBounds.HasValue) {
            return;
        }

        Vector3 boardCenter = gridBounds.Value.center;
        Vector3 cameraOffset = normalCameraPosition - boardCenter;
        if (cameraOffset.sqrMagnitude <= Mathf.Epsilon) {
            return;
        }

        Vector3 targetPosition = boardCenter + cameraOffset * GameEndCameraDistanceFactor;
        StartAnimation(cameraTransform.position, targetPosition, GameEndCameraTransitionSeconds);
        isGameEndCameraActive = true;
    }

    private void StartRestoreCameraAnimation()
    {
        if (!isGameEndCameraActive && !isAnimating) {
            return;
        }

        Transform cameraTransform = GetDuelCameraTransform();
        if (cameraTransform == null || !hasNormalCameraPosition) {
            return;
        }

        StartAnimation(cameraTransform.position, normalCameraPosition, RestoreCameraTransitionSeconds);
        isGameEndCameraActive = false;
    }

    private void StartAnimation(Vector3 startPosition, Vector3 targetPosition, float durationSeconds)
    {
        animationStartPosition = startPosition;
        animationTargetPosition = targetPosition;
        animationDurationSeconds = Mathf.Max(durationSeconds, 0.01f);
        animationElapsedSeconds = 0f;
        isAnimating = true;
    }

    private void UpdateCameraAnimation()
    {
        if (!isAnimating) {
            return;
        }

        Transform cameraTransform = GetDuelCameraTransform();
        if (cameraTransform == null) {
            isAnimating = false;
            return;
        }

        animationElapsedSeconds += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(animationElapsedSeconds / animationDurationSeconds);
        float easedT = t * t * (3f - 2f * t);
        cameraTransform.position = Vector3.Lerp(animationStartPosition, animationTargetPosition, easedT);

        if (t >= 1f) {
            cameraTransform.position = animationTargetPosition;
            isAnimating = false;
        }
    }

    private Transform GetDuelCameraTransform()
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        return compChessBoard?.duelVCam != null ? compChessBoard.duelVCam.transform : null;
    }

    private Bounds? GetGridBounds()
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard?.chessBoardGrid == null) {
            return null;
        }

        return compChessBoard.chessBoardGrid.GetGridBounds();
    }
}
