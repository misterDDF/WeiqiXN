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
    private const int MoveNumberMarkerFontSize = 48;
    private const float MoveNumberMarkerCharacterSize = 0.46f;
    private const float LatestMoveMarkerSizeFactor = ChessBoardConfig.starPointRadiusFactor * 2f * 2.3f;
    private static readonly Color MoveNumberOnBlackStoneColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color MoveNumberOnWhiteStoneColor = new Color(0f, 0f, 0f, 1f);
    private static Mesh latestMoveMarkerMesh;

    private int bindVersion;
    private int posIndex = -1;
    private PlayerFlag playerFlag;
    private bool placementAnimationDone = true;
    private StoneMarkerIntent pendingMarker;
    private GameObject markerRoot;
    private Coroutine placementAnimationCoroutine;
    private Material latestMoveMarkerOnBlackStoneMaterial;
    private Material latestMoveMarkerOnWhiteStoneMaterial;

    public void SetLatestMoveMarkerMaterials(Material onBlackStoneMaterial, Material onWhiteStoneMaterial)
    {
        latestMoveMarkerOnBlackStoneMaterial = onBlackStoneMaterial;
        latestMoveMarkerOnWhiteStoneMaterial = onWhiteStoneMaterial;
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
        } else if (marker.markerType == StoneMarkerType.LatestTriangle) {
            ShowLatestTriangleMarker(marker);
        }
    }

    private void ShowMoveNumberMarker(StoneMarkerIntent marker)
    {
        if (marker.moveNumber <= 0) {
            return;
        }

        GameObject root = EnsureMarkerRoot();
        GameObject labelGO = new GameObject($"MoveNumber_{marker.moveNumber}");
        labelGO.transform.SetParent(root.transform, false);
        labelGO.transform.localPosition = Vector3.zero;
        labelGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        labelGO.transform.localScale = Vector3.one;

        TextMesh textMesh = labelGO.AddComponent<TextMesh>();
        textMesh.text = marker.moveNumber.ToString();
        textMesh.fontSize = MoveNumberMarkerFontSize;
        textMesh.characterSize = MoveNumberMarkerCharacterSize;
        textMesh.fontStyle = FontStyle.Bold;
        textMesh.alignment = TextAlignment.Center;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.color = marker.isBlackStone ? MoveNumberOnBlackStoneColor : MoveNumberOnWhiteStoneColor;
        textMesh.richText = false;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) {
            textMesh.font = font;
        }

        MeshRenderer meshRenderer = labelGO.GetComponent<MeshRenderer>();
        if (meshRenderer != null) {
            meshRenderer.receiveShadows = false;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.sortingOrder = 20;
        }
    }

    private void ShowLatestTriangleMarker(StoneMarkerIntent marker)
    {
        Material markerMaterial = marker.isBlackStone
            ? latestMoveMarkerOnBlackStoneMaterial
            : latestMoveMarkerOnWhiteStoneMaterial;
        if (markerMaterial == null) {
            return;
        }

        GameObject root = EnsureMarkerRoot();
        GameObject markerGO = new GameObject("LatestMoveTriangle");
        markerGO.transform.SetParent(root.transform, false);
        markerGO.transform.localPosition = Vector3.zero;
        markerGO.transform.localRotation = Quaternion.identity;
        markerGO.transform.localScale = Vector3.one;

        MeshFilter meshFilter = markerGO.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = GetLatestMoveMarkerMesh();

        MeshRenderer meshRenderer = markerGO.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = markerMaterial;
        meshRenderer.receiveShadows = false;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.sortingOrder = 20;
    }

    private GameObject EnsureMarkerRoot()
    {
        if (markerRoot != null) {
            return markerRoot;
        }

        markerRoot = new GameObject("StoneMarkerRoot");
        markerRoot.transform.SetParent(transform, false);
        markerRoot.transform.localPosition = new Vector3(0f, MarkerLocalYOffset, 0f);
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

    private static Mesh GetLatestMoveMarkerMesh()
    {
        if (latestMoveMarkerMesh != null) {
            return latestMoveMarkerMesh;
        }

        float markerSize = ChessBoardConfig.rectCellSideLength * LatestMoveMarkerSizeFactor;
        float halfWidth = markerSize * 0.5f;
        float halfHeight = markerSize * 0.5f;
        latestMoveMarkerMesh = new Mesh();
        latestMoveMarkerMesh.name = "StoneLatestMoveMarkerMesh";
        latestMoveMarkerMesh.SetVertices(new List<Vector3>
        {
            new Vector3(0f, 0f, halfHeight),
            new Vector3(-halfWidth, 0f, -halfHeight),
            new Vector3(halfWidth, 0f, -halfHeight),
        });
        latestMoveMarkerMesh.SetTriangles(new[] { 0, 2, 1 }, 0);
        latestMoveMarkerMesh.RecalculateNormals();
        return latestMoveMarkerMesh;
    }

    private void OnDisable()
    {
        StopPlacementAnimationWait();
    }

    private void OnDestroy()
    {
        StopPlacementAnimationWait();
        ClearMarkerVisual();
    }
}
