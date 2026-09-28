using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNClient.ChessBoard;

public class ChessStoneView : MonoBehaviour
{
    // 落子：从高处加速落下，着盘后绕底面接触点摇晃并衰减。
    // 正交俯视下看不到棋子本身的高度变化，下落主要靠投影从远处滑到子下方来表现。
    public const float PlacementDropSeconds = 0.1f;
    private const float PlacementSettleSeconds = 0.4f;
    private const float PlacementDropHeight = 5f;
    // 摇晃沿用美化前 Animator 动画的幅度与节奏：包络 (1-s)^1.5 下 sin(7πs) 的首个峰值约为 0.897，
    // 振幅 15° 时首个峰值约 13.5°，之后每半周期约 57ms 逐次减弱。正交俯视下几度的倾角看不出来，不宜再调小。
    private const float PlacementTiltAmplitudeDegrees = 15f;
    private const float PlacementTiltHalfCycles = 7f;
    // 下落途中投影随高度变虚、变淡；离地后子边遮蔽很快消失。
    private const float ShadowSoftnessPerHeight = 0.3f;
    private const float ShadowStrengthFalloffPerHeight = 0.15f;
    private const float ShadowContactFadeHeight = 0.5f;
    private const float ShadowCoverageMargin = 0.2f;
    private const string ModelNodePath = "VisualOffset/Model";
    private const float MarkerLocalYOffset = 1.74f;
    // 最后一手圆点出现时 160ms 淡入；手数数字不淡入。
    private const float LatestMoveMarkerFadeSeconds = 0.16f;

    private static readonly int ShadowContactStrengthId = Shader.PropertyToID("_ContactStrength");
    private static readonly int ShadowCastStrengthId = Shader.PropertyToID("_CastStrength");
    private static readonly int ShadowCastHeightId = Shader.PropertyToID("_CastHeight");
    private static readonly int ShadowCastSoftnessId = Shader.PropertyToID("_CastSoftness");
    private static readonly int ShadowStoneRadiusId = Shader.PropertyToID("_StoneRadius");
    private static readonly int MarkerColorId = Shader.PropertyToID("_Color");

    private int bindVersion;
    private int posIndex = -1;
    private PlayerFlag playerFlag;
    private bool placementAnimationDone = true;
    private StoneMarkerIntent pendingMarker;
    private GameObject markerRoot;
    private Coroutine placementCoroutine;
    private Coroutine removalCoroutine;
    private Coroutine markerFadeCoroutine;
    private MaterialPropertyBlock markerBlock;
    private Material latestMoveMarkerOnBlackStoneMaterial;
    private Material latestMoveMarkerOnWhiteStoneMaterial;
    private Material removedStonePreviewMaterial;
    private ChessStoneVisualRandomizer visualRandomizer;
    private readonly Dictionary<Renderer, Material[]> originalRendererMaterials = new Dictionary<Renderer, Material[]>();

    private bool placementNodesResolved;
    private bool isMotionPoseApplied;
    private Transform placementModel;
    private Vector3 modelRestPosition;
    private Quaternion modelRestRotation;
    private Renderer contactShadowRenderer;
    private Vector3 contactShadowRestScale;
    private float contactShadowRestHalfExtent;
    private MaterialPropertyBlock contactShadowBlock;

    public void SetLatestMoveMarkerMaterials(Material onBlackStoneMaterial, Material onWhiteStoneMaterial)
    {
        latestMoveMarkerOnBlackStoneMaterial = onBlackStoneMaterial;
        latestMoveMarkerOnWhiteStoneMaterial = onWhiteStoneMaterial;
    }

    public void SetRemovedStonePreviewMaterial(Material previewMaterial)
    {
        removedStonePreviewMaterial = previewMaterial;
    }

    public void Bind(int posIndex, PlayerFlag playerFlag, bool playPlacement)
    {
        // 等待提子隐藏期间被重新显示（打劫、试下后退）时取消隐藏。
        StopPendingRemoval();
        bool isSameBinding = this.posIndex == posIndex && this.playerFlag == playerFlag;
        this.posIndex = posIndex;
        this.playerFlag = playerFlag;

        // 同一颗棋子重复同步且不要求落子动画时保持原状，正在播放的落子动画继续播完。
        if (isSameBinding && !playPlacement) {
            return;
        }

        bindVersion += 1;
        ClearMarkerVisual();
        pendingMarker = default;
        SetRemovedVisual(false);
        StopPlacementAnimation();
        if (playPlacement && isActiveAndEnabled) {
            placementAnimationDone = false;
            placementCoroutine = StartCoroutine(PlayPlacement(bindVersion));
        }
    }

    public void Unbind()
    {
        bindVersion += 1;
        posIndex = -1;
        playerFlag = 0;
        pendingMarker = default;
        StopPlacementAnimation();
        StopPendingRemoval();
        placementAnimationDone = false;
        ClearMarkerVisual();
        SetRemovedVisual(false);
    }

    // 提子：解除绑定后保留 delay 秒（到落子着盘、提子音响起）再瞬间隐藏自身，沿用美化前的提子表现。
    public void RemoveAfterDelay(float delay)
    {
        Unbind();
        if (delay <= 0f || !isActiveAndEnabled) {
            gameObject.SetActive(false);
            return;
        }

        removalCoroutine = StartCoroutine(RemoveAfterDelayRoutine(delay));
    }

    public void SetMarker(StoneMarkerIntent marker)
    {
        pendingMarker = marker;
        if (!marker.IsValid) {
            ClearMarkerVisual();
            return;
        }

        if (placementAnimationDone) {
            ShowMarker(marker);
        } else {
            ClearMarkerVisual();
        }
    }

    public void ClearMarker()
    {
        pendingMarker = default;
        ClearMarkerVisual();
    }

    public void SetRemovedVisual(bool removed)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool showRemovedVisual = removed && removedStonePreviewMaterial != null;
        foreach (Renderer renderer in renderers) {
            if (IsContactShadow(renderer)) {
                renderer.enabled = !showRemovedVisual;
            }
        }

        if (!removed) {
            foreach (Renderer renderer in renderers) {
                if (renderer != null && originalRendererMaterials.TryGetValue(renderer, out Material[] materials)) {
                    renderer.sharedMaterials = materials;
                }
            }
            originalRendererMaterials.Clear();
            return;
        }

        if (removedStonePreviewMaterial == null) {
            return;
        }

        foreach (Renderer renderer in renderers) {
            if (renderer == null || IsContactShadow(renderer)) {
                continue;
            }

            if (!originalRendererMaterials.ContainsKey(renderer)) {
                originalRendererMaterials[renderer] = renderer.sharedMaterials;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) {
                continue;
            }

            Material[] previewMaterials = new Material[materials.Length];
            for (int i = 0; i < previewMaterials.Length; i++) {
                previewMaterials[i] = removedStonePreviewMaterial;
            }
            renderer.sharedMaterials = previewMaterials;
        }
    }

    private static bool IsContactShadow(Renderer renderer)
    {
        return renderer != null && renderer.GetComponent<ChessStoneContactShadow>() != null;
    }

    // 落子开始后 elapsed 秒时棋子离盘高度与摇晃倾角（度）：先按 t² 加速落下，着盘后按 (1-s)^1.5 包络的正弦摇晃衰减到 0。
    public static void EvaluatePlacement(float elapsed, out float height, out float tiltDegrees)
    {
        if (elapsed < PlacementDropSeconds) {
            float u = Mathf.Clamp01(elapsed / PlacementDropSeconds);
            height = PlacementDropHeight * (1f - u * u);
            tiltDegrees = 0f;
            return;
        }

        float s = Mathf.Clamp01((elapsed - PlacementDropSeconds) / PlacementSettleSeconds);
        float envelope = (1f - s) * Mathf.Sqrt(1f - s);
        height = 0f;
        tiltDegrees = PlacementTiltAmplitudeDegrees * envelope * Mathf.Sin(s * PlacementTiltHalfCycles * Mathf.PI);
    }

    // 按落子开始后的时间摆出棋子姿态与投影；截图工具也用它按关键帧采样。
    public void ApplyPlacementPose(float elapsed, Vector3 tiltAxis)
    {
        if (!ResolvePlacementNodes()) {
            return;
        }

        EvaluatePlacement(elapsed, out float height, out float tiltDegrees);
        // 绕棋子底面中心（与盘面的接触点）转：小角度下等同于在凸面底上滚动，不会悬空，也几乎不穿入盘面。
        Quaternion tilt = Quaternion.AngleAxis(tiltDegrees, tiltAxis);
        placementModel.localPosition = tilt * modelRestPosition + Vector3.up * height;
        placementModel.localRotation = tilt * modelRestRotation;
        ApplyContactShadow(height);
        isMotionPoseApplied = true;
    }

    private IEnumerator PlayPlacement(int targetBindVersion)
    {
        float tiltAngle = Random.Range(0f, Mathf.PI * 2f);
        Vector3 tiltAxis = new Vector3(Mathf.Cos(tiltAngle), 0f, Mathf.Sin(tiltAngle));
        float duration = PlacementDropSeconds + PlacementSettleSeconds;
        float elapsed = 0f;
        ApplyPlacementPose(elapsed, tiltAxis);

        while (elapsed < duration) {
            yield return null;
            elapsed += Time.deltaTime;
            ApplyPlacementPose(elapsed, tiltAxis);
            if (!placementAnimationDone && elapsed >= PlacementDropSeconds) {
                CompletePlacementDrop(targetBindVersion);
            }
        }

        placementCoroutine = null;
        ResetMotionPose();
    }

    private IEnumerator RemoveAfterDelayRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        removalCoroutine = null;
        gameObject.SetActive(false);
    }

    // 离盘时投影沿主光方向偏离子下方，并随高度变虚变淡；参数用 MaterialPropertyBlock 覆盖，回到静止后清除以保持合批。
    private void ApplyContactShadow(float height)
    {
        if (contactShadowRenderer == null) {
            return;
        }

        Material material = contactShadowRenderer.sharedMaterial;
        if (height <= 0f || material == null) {
            contactShadowRenderer.SetPropertyBlock(null);
            contactShadowRenderer.transform.localScale = contactShadowRestScale;
            return;
        }

        float stoneRadius = material.GetFloat(ShadowStoneRadiusId);
        float castHeight = material.GetFloat(ShadowCastHeightId) + height;
        float castSoftness = material.GetFloat(ShadowCastSoftnessId) + height * ShadowSoftnessPerHeight;
        if (contactShadowBlock == null) {
            contactShadowBlock = new MaterialPropertyBlock();
        }
        contactShadowBlock.SetFloat(ShadowContactStrengthId, material.GetFloat(ShadowContactStrengthId) * Mathf.Clamp01(1f - height / ShadowContactFadeHeight));
        contactShadowBlock.SetFloat(ShadowCastStrengthId, material.GetFloat(ShadowCastStrengthId) / (1f + height * ShadowStrengthFalloffPerHeight));
        contactShadowBlock.SetFloat(ShadowCastHeightId, castHeight);
        contactShadowBlock.SetFloat(ShadowCastSoftnessId, castSoftness);
        contactShadowRenderer.SetPropertyBlock(contactShadowBlock);

        // 面片要盖住偏移后的整块投影；阴影距离按世界单位计算，放大面片只扩大覆盖范围，不改变阴影形状。
        float requiredHalfExtent = GetCastOffsetPerHeight() * castHeight
            + stoneRadius
            + castSoftness * 0.5f
            + ShadowCoverageMargin;
        float coverageScale = Mathf.Max(1f, requiredHalfExtent / contactShadowRestHalfExtent);
        contactShadowRenderer.transform.localScale = contactShadowRestScale * coverageScale;
    }

    // 与 StoneContactShadow.shader 的投影偏移一致：-lightDir.xz / max(lightDir.y, 0.25) × 高度。
    private static float GetCastOffsetPerHeight()
    {
        Light sun = RenderSettings.sun;
        if (sun == null) {
            return 1f;
        }

        Vector3 toLight = -sun.transform.forward;
        return new Vector2(toLight.x, toLight.z).magnitude / Mathf.Max(toLight.y, 0.25f);
    }

    private bool ResolvePlacementNodes()
    {
        if (placementNodesResolved) {
            return placementModel != null;
        }

        placementNodesResolved = true;
        placementModel = transform.Find(ModelNodePath);
        if (placementModel != null) {
            modelRestPosition = placementModel.localPosition;
            modelRestRotation = placementModel.localRotation;
        }

        ChessStoneContactShadow contactShadow = GetComponentInChildren<ChessStoneContactShadow>(true);
        contactShadowRenderer = contactShadow != null ? contactShadow.GetComponent<Renderer>() : null;
        MeshFilter contactShadowMesh = contactShadow != null ? contactShadow.GetComponent<MeshFilter>() : null;
        if (contactShadowRenderer != null && contactShadowMesh != null && contactShadowMesh.sharedMesh != null) {
            Transform shadowTransform = contactShadowRenderer.transform;
            contactShadowRestScale = shadowTransform.localScale;
            contactShadowRestHalfExtent = Mathf.Max(contactShadowMesh.sharedMesh.bounds.extents.x * shadowTransform.lossyScale.x, 0.01f);
        } else {
            contactShadowRenderer = null;
        }

        return placementModel != null;
    }

    private void ResetMotionPose()
    {
        if (!isMotionPoseApplied) {
            return;
        }

        isMotionPoseApplied = false;
        if (placementModel != null) {
            placementModel.localPosition = modelRestPosition;
            placementModel.localRotation = modelRestRotation;
        }
        ApplyContactShadow(0f);
    }

    private void CompletePlacementDrop(int targetBindVersion)
    {
        if (targetBindVersion != bindVersion) {
            return;
        }

        placementAnimationDone = true;
        if (pendingMarker.IsValid) {
            ShowMarker(pendingMarker);
        }
    }

    private void StopPlacementAnimation()
    {
        if (placementCoroutine != null) {
            StopCoroutine(placementCoroutine);
            placementCoroutine = null;
        }

        placementAnimationDone = true;
        ResetMotionPose();
    }

    private void StopPendingRemoval()
    {
        if (removalCoroutine == null) {
            return;
        }

        StopCoroutine(removalCoroutine);
        removalCoroutine = null;
    }

    private void ShowMarker(StoneMarkerIntent marker)
    {
        ClearMarkerVisual();
        if (!marker.IsValid) {
            return;
        }

        if (marker.markerType == StoneMarkerType.MoveNumber) {
            ShowMoveNumberMarker(marker);
        } else if (marker.markerType == StoneMarkerType.LatestMove) {
            ShowLatestMoveMarker(marker);
        }
    }

    private void ShowMoveNumberMarker(StoneMarkerIntent marker)
    {
        if (marker.moveNumber <= 0) {
            return;
        }

        GameObject root = EnsureMarkerRoot();
        BoardSurfaceMarker.CreateMoveNumber(
            root.transform,
            $"MoveNumber_{marker.moveNumber}",
            marker.moveNumber,
            marker.isBlackStone,
            Vector3.zero);
    }

    private void ShowLatestMoveMarker(StoneMarkerIntent marker)
    {
        Material markerMaterial = marker.isBlackStone
            ? latestMoveMarkerOnBlackStoneMaterial
            : latestMoveMarkerOnWhiteStoneMaterial;
        if (markerMaterial == null) {
            return;
        }

        GameObject root = EnsureMarkerRoot();
        MeshRenderer markerRenderer = BoardSurfaceMarker.CreateLatestMoveMarker(root.transform, "LatestMoveMarker", Vector3.zero, markerMaterial);
        // 编辑器截图不推进协程，只在运行时淡入。
        if (markerRenderer != null && Application.isPlaying && isActiveAndEnabled && markerMaterial.HasProperty(MarkerColorId)) {
            markerFadeCoroutine = StartCoroutine(FadeInMarker(markerRenderer, markerMaterial.GetColor(MarkerColorId)));
        }
    }

    private IEnumerator FadeInMarker(Renderer markerRenderer, Color baseColor)
    {
        if (markerBlock == null) {
            markerBlock = new MaterialPropertyBlock();
        }

        float elapsed = 0f;
        while (elapsed < LatestMoveMarkerFadeSeconds) {
            Color color = baseColor;
            color.a *= elapsed / LatestMoveMarkerFadeSeconds;
            markerBlock.SetColor(MarkerColorId, color);
            markerRenderer.SetPropertyBlock(markerBlock);
            yield return null;
            elapsed += Time.deltaTime;
        }

        markerRenderer.SetPropertyBlock(null);
        markerFadeCoroutine = null;
    }

    private GameObject EnsureMarkerRoot()
    {
        if (markerRoot != null) {
            return markerRoot;
        }

        markerRoot = new GameObject("StoneMarkerRoot");
        markerRoot.transform.SetParent(transform, false);
        markerRoot.transform.localPosition = GetVisualPositionOffset() + new Vector3(0f, MarkerLocalYOffset, 0f);
        markerRoot.transform.localRotation = Quaternion.identity;
        markerRoot.transform.localScale = Vector3.one;
        return markerRoot;
    }

    private void ClearMarkerVisual()
    {
        if (markerFadeCoroutine != null) {
            StopCoroutine(markerFadeCoroutine);
            markerFadeCoroutine = null;
        }

        if (markerRoot == null) {
            return;
        }

        Destroy(markerRoot);
        markerRoot = null;
    }

    // 标记居中到随机摆放后的棋子上，但不跟随偏转，手数始终保持正向。
    private Vector3 GetVisualPositionOffset()
    {
        if (visualRandomizer == null) {
            visualRandomizer = GetComponent<ChessStoneVisualRandomizer>();
        }

        return visualRandomizer != null ? visualRandomizer.PositionOffset : Vector3.zero;
    }

    private void OnDisable()
    {
        StopPlacementAnimation();
        StopPendingRemoval();
    }

    private void OnDestroy()
    {
        StopPlacementAnimation();
        StopPendingRemoval();
        ClearMarkerVisual();
        SetRemovedVisual(false);
    }
}
