using Cinemachine;
using UnityEngine;

public class DuelGameEndCameraSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelGameEndCameraSystem>();

    private const float GameEndCameraDistanceFactor = 1.35f;
    private const float GameEndCameraTransitionSeconds = 1.5f;
    private const float RestoreCameraTransitionSeconds = 0.35f;

    private bool hasNormalCameraPosition;
    private bool hasNormalOrthographicSize;
    private bool isAnimating;
    private bool isGameEndCameraActive;
    private Vector3 normalCameraPosition;
    private float normalOrthographicSize;
    private Vector3 animationStartPosition;
    private Vector3 animationTargetPosition;
    private float animationStartOrthographicSize;
    private float animationTargetOrthographicSize;
    private float animationElapsedSeconds;
    private float animationDurationSeconds;

    public DuelGameEndCameraSystem(SceneBase scene) : base(scene)
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
        CinemachineVirtualCamera duelCamera = GetDuelCamera();
        if (duelCamera == null) {
            hasNormalCameraPosition = false;
            hasNormalOrthographicSize = false;
            return;
        }

        normalCameraPosition = duelCamera.transform.position;
        normalOrthographicSize = duelCamera.m_Lens.OrthographicSize;
        hasNormalCameraPosition = true;
        hasNormalOrthographicSize = true;
    }

    private void StartGameEndCameraAnimation()
    {
        if (!hasNormalCameraPosition) {
            CaptureNormalCameraPosition();
        }

        CinemachineVirtualCamera duelCamera = GetDuelCamera();
        Bounds? gridBounds = GetGridBounds();
        if (duelCamera == null || !hasNormalCameraPosition || !gridBounds.HasValue) {
            return;
        }

        Vector3 boardCenter = gridBounds.Value.center;
        Vector3 cameraOffset = normalCameraPosition - boardCenter;
        if (cameraOffset.sqrMagnitude <= Mathf.Epsilon) {
            return;
        }

        Vector3 targetPosition = boardCenter + cameraOffset * GameEndCameraDistanceFactor;
        float targetOrthographicSize = hasNormalOrthographicSize
            ? normalOrthographicSize * GameEndCameraDistanceFactor
            : duelCamera.m_Lens.OrthographicSize;
        StartAnimation(
            duelCamera.transform.position,
            targetPosition,
            duelCamera.m_Lens.OrthographicSize,
            targetOrthographicSize,
            GameEndCameraTransitionSeconds);
        isGameEndCameraActive = true;
    }

    private void StartRestoreCameraAnimation()
    {
        if (!isGameEndCameraActive && !isAnimating) {
            return;
        }

        CinemachineVirtualCamera duelCamera = GetDuelCamera();
        if (duelCamera == null || !hasNormalCameraPosition) {
            return;
        }

        float targetOrthographicSize = hasNormalOrthographicSize
            ? normalOrthographicSize
            : duelCamera.m_Lens.OrthographicSize;
        StartAnimation(
            duelCamera.transform.position,
            normalCameraPosition,
            duelCamera.m_Lens.OrthographicSize,
            targetOrthographicSize,
            RestoreCameraTransitionSeconds);
        isGameEndCameraActive = false;
    }

    private void StartAnimation(
        Vector3 startPosition,
        Vector3 targetPosition,
        float startOrthographicSize,
        float targetOrthographicSize,
        float durationSeconds)
    {
        animationStartPosition = startPosition;
        animationTargetPosition = targetPosition;
        animationStartOrthographicSize = startOrthographicSize;
        animationTargetOrthographicSize = targetOrthographicSize;
        animationDurationSeconds = Mathf.Max(durationSeconds, 0.01f);
        animationElapsedSeconds = 0f;
        isAnimating = true;
    }

    private void UpdateCameraAnimation()
    {
        if (!isAnimating) {
            return;
        }

        CinemachineVirtualCamera duelCamera = GetDuelCamera();
        if (duelCamera == null) {
            isAnimating = false;
            return;
        }

        animationElapsedSeconds += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(animationElapsedSeconds / animationDurationSeconds);
        float easedT = t * t * (3f - 2f * t);
        duelCamera.transform.position = Vector3.Lerp(animationStartPosition, animationTargetPosition, easedT);
        ApplyOrthographicSize(duelCamera, Mathf.Lerp(animationStartOrthographicSize, animationTargetOrthographicSize, easedT));

        if (t >= 1f) {
            duelCamera.transform.position = animationTargetPosition;
            ApplyOrthographicSize(duelCamera, animationTargetOrthographicSize);
            isAnimating = false;
        }
    }

    private void ApplyOrthographicSize(CinemachineVirtualCamera duelCamera, float orthographicSize)
    {
        if (duelCamera == null) {
            return;
        }

        LensSettings lens = duelCamera.m_Lens;
        lens.OrthographicSize = orthographicSize;
        duelCamera.m_Lens = lens;
    }

    private CinemachineVirtualCamera GetDuelCamera()
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        return compChessBoard?.duelVCam;
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
