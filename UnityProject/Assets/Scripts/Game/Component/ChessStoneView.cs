using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNClient.ChessBoard;

public class ChessStoneView : MonoBehaviour
{
    private const float MarkerLocalYOffset = 1.74f;
    private const float MarkerAnimationMaxWaitSeconds = 2f;
    private const float MarkerAnimationMinimumWaitSeconds = 0.05f;
    private const float PlacementDropCompleteNormalizedTime = 0.35f;

    private int bindVersion;
    private int posIndex = -1;
    private PlayerFlag playerFlag;
    private bool placementAnimationDone = true;
    private StoneMarkerIntent pendingMarker;
    private GameObject markerRoot;
    private Coroutine placementAnimationCoroutine;
    private Material latestMoveMarkerOnBlackStoneMaterial;
    private Material latestMoveMarkerOnWhiteStoneMaterial;
    private Material removedStonePreviewMaterial;
    private ChessStoneVisualRandomizer visualRandomizer;
    private readonly Dictionary<Renderer, Material[]> originalRendererMaterials = new Dictionary<Renderer, Material[]>();

    public void SetLatestMoveMarkerMaterials(Material onBlackStoneMaterial, Material onWhiteStoneMaterial)
    {
        latestMoveMarkerOnBlackStoneMaterial = onBlackStoneMaterial;
        latestMoveMarkerOnWhiteStoneMaterial = onWhiteStoneMaterial;
    }

    public void SetRemovedStonePreviewMaterial(Material previewMaterial)
    {
        removedStonePreviewMaterial = previewMaterial;
    }

    public void Bind(int posIndex, PlayerFlag playerFlag, bool waitForPlacementAnimation)
    {
        bool isSameBinding = this.posIndex == posIndex && this.playerFlag == playerFlag;
        this.posIndex = posIndex;
        this.playerFlag = playerFlag;

        if (!isSameBinding || waitForPlacementAnimation) {
            bindVersion += 1;
            ClearMarkerVisual();
            pendingMarker = default;
            SetRemovedVisual(false);
        }

        placementAnimationDone = !waitForPlacementAnimation;
        StopPlacementAnimationWait();
        if (waitForPlacementAnimation && isActiveAndEnabled) {
            placementAnimationCoroutine = StartCoroutine(WaitForPlacementAnimation(bindVersion));
        }
    }

    public void Unbind()
    {
        bindVersion += 1;
        posIndex = -1;
        playerFlag = 0;
        placementAnimationDone = false;
        pendingMarker = default;
        StopPlacementAnimationWait();
        ClearMarkerVisual();
        SetRemovedVisual(false);
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

    public void NotifyPlacementAnimationComplete()
    {
        NotifyPlacementDropComplete();
    }

    public void NotifyPlacementDropComplete()
    {
        CompletePlacementDrop(bindVersion);
    }

    private IEnumerator WaitForPlacementAnimation(int targetBindVersion)
    {
        yield return null;

        float elapsed = 0f;
        Animator animator = GetComponentInChildren<Animator>();
        Animation legacyAnimation = animator == null ? GetComponentInChildren<Animation>() : null;

        while (elapsed < MarkerAnimationMaxWaitSeconds) {
            if (targetBindVersion != bindVersion) {
                yield break;
            }

            bool canComplete = elapsed >= MarkerAnimationMinimumWaitSeconds;
            if (canComplete && IsPlacementDropComplete(animator, legacyAnimation)) {
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        CompletePlacementDrop(targetBindVersion);
    }

    private bool IsPlacementDropComplete(Animator animator, Animation legacyAnimation)
    {
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null) {
            if (animator.IsInTransition(0)) {
                return false;
            }

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            return stateInfo.normalizedTime >= PlacementDropCompleteNormalizedTime;
        }

        if (legacyAnimation != null && legacyAnimation.isActiveAndEnabled) {
            foreach (AnimationState state in legacyAnimation) {
                if (state.enabled && state.length > 0f && state.time / state.length >= PlacementDropCompleteNormalizedTime) {
                    return true;
                }
            }

            return !legacyAnimation.isPlaying;
        }

        return true;
    }

    private void CompletePlacementDrop(int targetBindVersion)
    {
        if (targetBindVersion != bindVersion) {
            return;
        }

        placementAnimationDone = true;
        placementAnimationCoroutine = null;
        if (pendingMarker.IsValid) {
            ShowMarker(pendingMarker);
        }
    }

    private void StopPlacementAnimationWait()
    {
        if (placementAnimationCoroutine == null) {
            return;
        }

        StopCoroutine(placementAnimationCoroutine);
        placementAnimationCoroutine = null;
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
        BoardSurfaceMarker.CreateLatestMoveMarker(root.transform, "LatestMoveMarker", Vector3.zero, markerMaterial);
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
        StopPlacementAnimationWait();
    }

    private void OnDestroy()
    {
        StopPlacementAnimationWait();
        ClearMarkerVisual();
        SetRemovedVisual(false);
    }
}
