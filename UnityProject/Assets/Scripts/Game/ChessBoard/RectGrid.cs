using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.Logger;

namespace XNClient.ChessBoard
{
    public readonly struct RectGridMoveNumberMarker
    {
        public readonly int x;
        public readonly int z;
        public readonly int moveNumber;
        public readonly bool isBlackStone;

        public RectGridMoveNumberMarker(int x, int z, int moveNumber, bool isBlackStone)
        {
            this.x = x;
            this.z = z;
            this.moveNumber = moveNumber;
            this.isBlackStone = isBlackStone;
        }
    }

    public readonly struct RectGridAiRecommendationMarker
    {
        public readonly int x;
        public readonly int z;
        public readonly int winratePercent;
        public readonly int order;

        public RectGridAiRecommendationMarker(int x, int z, int winratePercent, int order)
        {
            this.x = x;
            this.z = z;
            this.winratePercent = winratePercent;
            this.order = order;
        }
    }

    public class RectGrid : MonoBehaviour
    {
        private readonly struct RectGridAiRecommendationMarkerDrawContext
        {
            public readonly RectGridAiRecommendationMarker marker;
            public readonly float alpha;

            public RectGridAiRecommendationMarkerDrawContext(RectGridAiRecommendationMarker marker, float alpha)
            {
                this.marker = marker;
                this.alpha = alpha;
            }
        }

        private readonly struct OwnershipPointDrawContext
        {
            public readonly int flag;
            public readonly float alpha;

            public OwnershipPointDrawContext(int flag, float alpha)
            {
                this.flag = flag;
                this.alpha = alpha;
            }
        }

        public GameObject chunkPrefab;
        public int gridSize;
        private List<RectGridChunk> chunkList = new List<RectGridChunk>();
        private List<RectCell> cellList = new List<RectCell>();
        private GameObject coordinateLabelRoot;
        private GameObject ownershipRoot;
        private GameObject latestMoveMarkerRoot;
        private GameObject moveNumberMarkerRoot;
        private GameObject aiRecommendationMarkerRoot;
        private bool boardCoordinateFrameVisible = true;

        private const float CoordinateLabelSurfaceYOffset = 0.04f;
        private const float CoordinateLabelBoundsPaddingFactor = 0.22f;
        private const float CoordinateLabelOuterOffsetFactor = 0.38f;
        private const int CoordinateLabelFontSize = 84;
        private const float CoordinateLabelCharacterSize = 0.32f;
        private const float CoordinateLabelShadowCharacterSize = 0.30f;
        private static readonly Vector3 CoordinateLabelShadowOffset = new Vector3(0.025f, -0.004f, -0.025f);
        private static readonly Color CoordinateLabelColor = new Color(0.23f, 0.15f, 0.07f, 0.98f);
        private static readonly Color CoordinateLabelShadowColor = new Color(0.08f, 0.05f, 0.02f, 0.55f);

        private const float OwnershipSquareSizeFactor = ChessBoardConfig.starPointRadiusFactor * 2f * 2.5f;
        private const float OwnershipYOffset = 0.04f;
        private const float OwnershipBlackPointMinAlpha = 0.55f;
        private const float OwnershipBlackPointMaxAlpha = 0.95f;
        private const float OwnershipWhitePointMinAlpha = 0.35f;
        private const float OwnershipWhitePointMaxAlpha = 0.75f;
        private const float LatestMoveMarkerYOffset = 0.05f;
        private const float LatestMoveMarkerSizeFactor = ChessBoardConfig.starPointRadiusFactor * 2f * 2.3f;
        private const float MoveNumberMarkerYOffset = 1.74f;
        private const int MoveNumberMarkerFontSize = 48;
        private const float MoveNumberMarkerCharacterSize = 0.46f;
        private const float AiRecommendationCircleYOffset = 0.10f;
        private const float AiRecommendationTextYOffset = 0.115f;
        private const float AiRecommendationMarkerSizeFactor = 0.96f;
        private const float AiRecommendationOutlineYOffset = 0.106f;
        private const float AiRecommendationOutlineWidth = 0.3f;
        private const int AiRecommendationCircleSegments = 40;
        private const int AiRecommendationFontSize = 58;
        private const float AiRecommendationCharacterSize = 0.46f;
        private const float AiRecommendationTextLeftOffset = 0.14f;
        private const int OwnershipNeutral = 0;
        private const int OwnershipBlack = 1;
        private const int OwnershipWhite = -1;
        private static readonly Color MoveNumberOnBlackStoneColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color MoveNumberOnWhiteStoneColor = new Color(0f, 0f, 0f, 1f);
        private const float AiRecommendationLowestAlpha = 0.35f;
        private const float AiRecommendationHighestAlpha = 0.85f;
        private static readonly Color AiRecommendationColor = new Color(0.05f, 0.85f, 0.20f, 1f);
        private static readonly Color AiRecommendationOutlineColor = new Color(0f, 0f, 0f, 0.92f);
        private static readonly Color AiRecommendationTextColor = new Color(0f, 0f, 0f, 1f);

        private Material blackMaterial;
        private Material whiteMaterial;
        private Material latestMoveMarkerOnBlackStoneMaterial;
        private Material latestMoveMarkerOnWhiteStoneMaterial;
        private Mesh latestMoveMarkerMesh;
        private Mesh aiRecommendationCircleMesh;
        private Mesh aiRecommendationCircleOutlineMesh;
        private Material aiRecommendationCircleMaterial;
        private Material aiRecommendationOutlineMaterial;

        public void InitGrid(int gridSize)
        {
            if (chunkPrefab == null || chunkPrefab.GetComponent<RectGridChunk>() == null) {
                XNLogger.LogError("Chunk prefab invalid, init grid failed.");
                return;
            }
            if (gridSize <= 0) {
                XNLogger.LogError("Grid size should be positive, init grid failed.", ("gridSize", gridSize.ToString()));
                return;
            }
            this.gridSize = gridSize;

            CreateChunks();
            CreateCells();
            RefreshBoardCoordinateFrame();
        }

        public void SetBoardMaterials(Material blackMaterial, Material whiteMaterial)
        {
            this.blackMaterial = blackMaterial;
            this.whiteMaterial = whiteMaterial;
        }

        public void SetLatestMoveMarkerMaterials(Material onBlackStoneMaterial, Material onWhiteStoneMaterial)
        {
            latestMoveMarkerOnBlackStoneMaterial = onBlackStoneMaterial;
            latestMoveMarkerOnWhiteStoneMaterial = onWhiteStoneMaterial;
        }

        public Bounds GetGridBounds()
        {
            EnsureCoordinateLabels();

            float gridSideLength = gridSize * ChessBoardConfig.rectCellSideLength;
            Vector3 localCenter = new Vector3(gridSideLength / 2f, 0f, gridSideLength / 2f);
            Vector3 worldCenter = transform.TransformPoint(localCenter);
            float coordinatePadding = boardCoordinateFrameVisible
                ? ChessBoardConfig.rectCellSideLength *
                    (ChessBoardVisualConfig.boardOuterBorderWidthFactor + CoordinateLabelBoundsPaddingFactor)
                : 0f;
            Vector3 size = new Vector3(gridSideLength + coordinatePadding * 2f, 0f, gridSideLength + coordinatePadding * 2f);
            return new Bounds(worldCenter, size);
        }

        public void SetBoardCoordinateFrameVisible(bool visible)
        {
            if (boardCoordinateFrameVisible == visible) {
                return;
            }

            boardCoordinateFrameVisible = visible;
            RefreshBoardCoordinateFrame();
        }

        public Vector3 GetCellCenterLocalPosition(int x, int z)
        {
            return new Vector3(
                (x + 0.5f) * ChessBoardConfig.rectCellSideLength,
                0f,
                (gridSize - z - 0.5f) * ChessBoardConfig.rectCellSideLength
            );
        }

        public void DrawOwnership(JArray ownership, float ownershipThreshold)
        {
            ClearOwnership();
            if (ownership == null) {
                return;
            }

            int expectedCount = gridSize * gridSize;
            if (ownership.Count < expectedCount) {
                XNLogger.LogError(
                    "Ownership length is smaller than board point count, draw skipped.",
                    ("ownershipCount", ownership.Count.ToString()),
                    ("expectedCount", expectedCount.ToString()));
                return;
            }

            ownershipRoot = new GameObject("OwnershipRoot");
            ownershipRoot.transform.SetParent(transform, false);

            OwnershipPointDrawContext[] ownershipPoints = BuildOwnershipPointDrawContexts(
                ownership,
                ownershipThreshold,
                expectedCount);
            float squareSize = ChessBoardConfig.rectCellSideLength * OwnershipSquareSizeFactor;
            for (int z = 0; z < gridSize; z++) {
                for (int x = 0; x < gridSize; x++) {
                    OwnershipPointDrawContext ownershipPoint = ownershipPoints[z * gridSize + x];
                    if (ownershipPoint.flag == OwnershipNeutral) {
                        continue;
                    }

                    Material material = GetOwnershipMaterial(ownershipPoint.flag);
                    if (material != null) {
                        CreateOwnershipSquare(x, z, squareSize, material, ownershipPoint.alpha);
                    }
                }
            }

        }

        public void ClearOwnership()
        {
            if (ownershipRoot == null) {
                return;
            }

            Destroy(ownershipRoot);
            ownershipRoot = null;
        }

        public void DrawLatestMoveMarker(int x, int z, bool isBlackStone)
        {
            ClearLatestMoveMarker();
            ClearMoveNumberMarkers();
            if (x < 0 || x >= gridSize || z < 0 || z >= gridSize) {
                XNLogger.LogError(
                    "Latest move marker position is outside board, draw skipped.",
                    ("x", x.ToString()),
                    ("z", z.ToString()),
                    ("gridSize", gridSize.ToString()));
                return;
            }

            latestMoveMarkerRoot = new GameObject("LatestMoveMarkerRoot");
            latestMoveMarkerRoot.transform.SetParent(transform, false);

            GameObject marker = new GameObject($"LatestMoveMarker_{x}_{z}");
            marker.transform.SetParent(latestMoveMarkerRoot.transform, false);
            marker.transform.localPosition = GetOwnershipLocalPosition(x, z, LatestMoveMarkerYOffset);

            MeshFilter meshFilter = marker.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetLatestMoveMarkerMesh();

            Material material = GetLatestMoveMarkerMaterial(isBlackStone);
            if (material == null) {
                ClearLatestMoveMarker();
                return;
            }

            MeshRenderer meshRenderer = marker.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
        }

        public void ClearLatestMoveMarker()
        {
            if (latestMoveMarkerRoot == null) {
                return;
            }

            Destroy(latestMoveMarkerRoot);
            latestMoveMarkerRoot = null;
        }

        public void DrawMoveNumberMarker(int x, int z, int moveNumber, bool isBlackStone)
        {
            DrawMoveNumberMarkers(new[]
            {
                new RectGridMoveNumberMarker(x, z, moveNumber, isBlackStone)
            });
        }

        public void DrawMoveNumberMarkers(IEnumerable<RectGridMoveNumberMarker> markers)
        {
            ClearMoveNumberMarkers();
            ClearLatestMoveMarker();
            if (markers == null) {
                return;
            }

            moveNumberMarkerRoot = new GameObject("MoveNumberMarkerRoot");
            moveNumberMarkerRoot.transform.SetParent(transform, false);

            foreach (RectGridMoveNumberMarker marker in markers) {
                if (marker.x < 0 || marker.x >= gridSize || marker.z < 0 || marker.z >= gridSize || marker.moveNumber <= 0) {
                    continue;
                }

                CreateMoveNumberMarker(marker);
            }

            if (moveNumberMarkerRoot.transform.childCount == 0) {
                ClearMoveNumberMarkers();
            }
        }

        public void ClearMoveNumberMarkers()
        {
            if (moveNumberMarkerRoot == null) {
                return;
            }

            Destroy(moveNumberMarkerRoot);
            moveNumberMarkerRoot = null;
        }

        public void DrawAiRecommendationMarkers(IEnumerable<RectGridAiRecommendationMarker> markers)
        {
            ClearAiRecommendationMarkers();
            if (markers == null) {
                return;
            }

            if (GetAiRecommendationMaterialShader() == null) {
                XNLogger.LogError("AI recommendation marker material source missing, draw skipped.");
                return;
            }

            aiRecommendationMarkerRoot = new GameObject("AiRecommendationMarkerRoot");
            aiRecommendationMarkerRoot.transform.SetParent(transform, false);

            List<RectGridAiRecommendationMarker> validMarkers = new List<RectGridAiRecommendationMarker>();
            foreach (RectGridAiRecommendationMarker marker in markers) {
                if (marker.x < 0 || marker.x >= gridSize || marker.z < 0 || marker.z >= gridSize) {
                    continue;
                }

                validMarkers.Add(marker);
            }

            foreach (RectGridAiRecommendationMarkerDrawContext markerContext in CreateAiRecommendationMarkerDrawContexts(validMarkers)) {
                CreateAiRecommendationMarker(markerContext);
            }

            if (aiRecommendationMarkerRoot.transform.childCount == 0) {
                ClearAiRecommendationMarkers();
            }
        }

        public void ClearAiRecommendationMarkers()
        {
            if (aiRecommendationMarkerRoot != null) {
                Destroy(aiRecommendationMarkerRoot);
                aiRecommendationMarkerRoot = null;
            }
        }

        private void CreateCoordinateLabels()
        {
            ClearCoordinateLabels();

            coordinateLabelRoot = new GameObject("CoordinateLabelRoot");
            coordinateLabelRoot.transform.SetParent(transform, false);

            float boardSideLength = gridSize * ChessBoardConfig.rectCellSideLength;
            float labelOuterOffset = ChessBoardConfig.rectCellSideLength *
                ChessBoardVisualConfig.boardOuterBorderWidthFactor *
                CoordinateLabelOuterOffsetFactor;
            for (int x = 0; x < gridSize; x++) {
                string columnLabel = GetGoCoordinateColumnLabel(x);
                float centerX = GetCellCenterLocalPosition(x, 0).x;
                CreateCoordinateLabel(
                    $"CoordinateTop_{columnLabel}",
                    columnLabel,
                    new Vector3(centerX, CoordinateLabelSurfaceYOffset, boardSideLength + labelOuterOffset));
            }

            for (int z = 0; z < gridSize; z++) {
                string rowLabel = (gridSize - z).ToString();
                float centerZ = GetCellCenterLocalPosition(0, z).z;
                CreateCoordinateLabel(
                    $"CoordinateLeft_{rowLabel}",
                    rowLabel,
                    new Vector3(-labelOuterOffset, CoordinateLabelSurfaceYOffset, centerZ));
            }
        }

        private void EnsureCoordinateLabels()
        {
            if (!boardCoordinateFrameVisible || gridSize <= 0 || coordinateLabelRoot != null) {
                return;
            }

            CreateCoordinateLabels();
        }

        private void RefreshBoardCoordinateFrame()
        {
            foreach (RectGridChunk chunk in chunkList) {
                if (chunk != null) {
                    chunk.SetOuterBorderVisible(boardCoordinateFrameVisible);
                }
            }

            if (boardCoordinateFrameVisible) {
                EnsureCoordinateLabels();
            } else {
                ClearCoordinateLabels();
            }
        }

        private void ClearCoordinateLabels()
        {
            if (coordinateLabelRoot == null) {
                return;
            }

            Destroy(coordinateLabelRoot);
            coordinateLabelRoot = null;
        }

        private void CreateCoordinateLabel(string objectName, string labelText, Vector3 localPosition)
        {
            if (coordinateLabelRoot == null) {
                return;
            }

            CreateCoordinateLabelMesh(
                objectName + "_Shadow",
                labelText,
                localPosition + CoordinateLabelShadowOffset,
                CoordinateLabelShadowCharacterSize,
                CoordinateLabelShadowColor,
                0);
            CreateCoordinateLabelMesh(
                objectName,
                labelText,
                localPosition,
                CoordinateLabelCharacterSize,
                CoordinateLabelColor,
                1);
        }

        private void CreateCoordinateLabelMesh(string objectName, string labelText, Vector3 localPosition, float characterSize, Color color, int sortingOrder)
        {
            GameObject labelGO = new GameObject(objectName);
            labelGO.transform.SetParent(coordinateLabelRoot.transform, false);
            labelGO.transform.localPosition = localPosition;
            labelGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            labelGO.transform.localScale = Vector3.one;

            TextMesh textMesh = labelGO.AddComponent<TextMesh>();
            textMesh.text = labelText;
            textMesh.fontSize = CoordinateLabelFontSize;
            textMesh.characterSize = characterSize;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.color = color;
            textMesh.richText = false;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) {
                textMesh.font = font;
            }

            MeshRenderer meshRenderer = labelGO.GetComponent<MeshRenderer>();
            if (meshRenderer != null) {
                meshRenderer.receiveShadows = false;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.sortingOrder = sortingOrder;
            }
        }

        private void CreateMoveNumberMarker(RectGridMoveNumberMarker marker)
        {
            GameObject labelGO = new GameObject($"MoveNumber_{marker.moveNumber}_{marker.x}_{marker.z}");
            labelGO.transform.SetParent(moveNumberMarkerRoot.transform, false);
            labelGO.transform.localPosition = GetOwnershipLocalPosition(marker.x, marker.z, MoveNumberMarkerYOffset);
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

        private void CreateAiRecommendationMarker(RectGridAiRecommendationMarkerDrawContext markerContext)
        {
            RectGridAiRecommendationMarker marker = markerContext.marker;
            int winratePercent = Mathf.Clamp(marker.winratePercent, 1, 100);
            CreateAiRecommendationCircle(marker, markerContext.alpha);
            CreateAiRecommendationCircleOutline(marker);
            CreateAiRecommendationText(marker, winratePercent);
        }

        private void CreateAiRecommendationCircle(RectGridAiRecommendationMarker marker, float alpha)
        {
            GameObject circle = new GameObject($"AiRecommendationCircle_{marker.order}_{marker.x}_{marker.z}");
            circle.transform.SetParent(aiRecommendationMarkerRoot.transform, false);
            circle.transform.localPosition = GetOwnershipLocalPosition(marker.x, marker.z, AiRecommendationCircleYOffset);
            circle.transform.localRotation = Quaternion.identity;
            circle.transform.localScale = Vector3.one;

            MeshFilter meshFilter = circle.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetAiRecommendationCircleMesh();

            Material material = GetAiRecommendationCircleMaterial();
            if (material == null) {
                return;
            }

            MeshRenderer meshRenderer = circle.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.receiveShadows = false;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.sortingOrder = 28;
            ApplyAiRecommendationColor(meshRenderer, material, ResolveAiRecommendationColor(alpha));
        }

        private void CreateAiRecommendationCircleOutline(RectGridAiRecommendationMarker marker)
        {
            GameObject outline = new GameObject($"AiRecommendationCircleOutline_{marker.order}_{marker.x}_{marker.z}");
            outline.transform.SetParent(aiRecommendationMarkerRoot.transform, false);
            outline.transform.localPosition = GetOwnershipLocalPosition(marker.x, marker.z, AiRecommendationOutlineYOffset);
            outline.transform.localRotation = Quaternion.identity;
            outline.transform.localScale = Vector3.one;

            MeshFilter meshFilter = outline.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetAiRecommendationCircleOutlineMesh();

            Material material = GetAiRecommendationOutlineMaterial();
            if (material == null) {
                return;
            }

            MeshRenderer meshRenderer = outline.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.receiveShadows = false;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.sortingOrder = 29;
            ApplyAiRecommendationColor(meshRenderer, material, AiRecommendationOutlineColor);
        }

        private void CreateAiRecommendationText(RectGridAiRecommendationMarker marker, int winratePercent)
        {
            string text = winratePercent.ToString();
            Vector3 localPosition = GetOwnershipLocalPosition(marker.x, marker.z, AiRecommendationTextYOffset);
            Vector3 numberPosition = localPosition + new Vector3(-AiRecommendationTextLeftOffset, 0f, 0f);
            CreateAiRecommendationTextMesh(
                $"AiRecommendationText_{marker.order}_{marker.x}_{marker.z}",
                text,
                numberPosition,
                AiRecommendationTextColor,
                30);
        }

        private void CreateAiRecommendationTextMesh(
            string objectName,
            string labelText,
            Vector3 localPosition,
            Color color,
            int sortingOrder,
            int fontSize = AiRecommendationFontSize,
            float characterSize = AiRecommendationCharacterSize)
        {
            GameObject labelGO = new GameObject(objectName);
            labelGO.transform.SetParent(aiRecommendationMarkerRoot.transform, false);
            labelGO.transform.localPosition = localPosition;
            labelGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            labelGO.transform.localScale = Vector3.one;

            TextMesh textMesh = labelGO.AddComponent<TextMesh>();
            textMesh.text = labelText;
            textMesh.fontSize = fontSize;
            textMesh.characterSize = characterSize;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.color = color;
            textMesh.richText = false;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) {
                textMesh.font = font;
            }

            MeshRenderer meshRenderer = labelGO.GetComponent<MeshRenderer>();
            if (meshRenderer != null) {
                meshRenderer.receiveShadows = false;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.sortingOrder = sortingOrder;
            }
        }

        private string GetGoCoordinateColumnLabel(int x)
        {
            int labelIndex = x < 8 ? x : x + 1;
            return ((char)('A' + labelIndex)).ToString();
        }

        private OwnershipPointDrawContext[] BuildOwnershipPointDrawContexts(
            JArray ownership,
            float ownershipThreshold,
            int expectedCount)
        {
            int[] ownershipFlags = new int[expectedCount];
            float[] ownershipStrengths = new float[expectedCount];
            float blackMinStrength = float.MaxValue;
            float blackMaxStrength = float.MinValue;
            float whiteMinStrength = float.MaxValue;
            float whiteMaxStrength = float.MinValue;

            for (int ownershipIndex = 0; ownershipIndex < expectedCount; ownershipIndex++) {
                if (!float.TryParse(ownership[ownershipIndex]?.ToString(), out float ownershipValue)) {
                    continue;
                }

                float ownershipStrength = Mathf.Abs(ownershipValue);
                if (ownershipStrength <= ownershipThreshold) {
                    continue;
                }

                int ownershipFlag = ownershipValue > 0f ? OwnershipBlack : OwnershipWhite;
                ownershipFlags[ownershipIndex] = ownershipFlag;
                ownershipStrengths[ownershipIndex] = ownershipStrength;
                if (ownershipFlag == OwnershipBlack) {
                    blackMinStrength = Mathf.Min(blackMinStrength, ownershipStrength);
                    blackMaxStrength = Mathf.Max(blackMaxStrength, ownershipStrength);
                } else {
                    whiteMinStrength = Mathf.Min(whiteMinStrength, ownershipStrength);
                    whiteMaxStrength = Mathf.Max(whiteMaxStrength, ownershipStrength);
                }
            }

            OwnershipPointDrawContext[] ownershipPoints = new OwnershipPointDrawContext[expectedCount];
            for (int ownershipIndex = 0; ownershipIndex < expectedCount; ownershipIndex++) {
                int ownershipFlag = ownershipFlags[ownershipIndex];
                if (ownershipFlag == OwnershipNeutral) {
                    continue;
                }

                float minStrength = ownershipFlag == OwnershipBlack ? blackMinStrength : whiteMinStrength;
                float maxStrength = ownershipFlag == OwnershipBlack ? blackMaxStrength : whiteMaxStrength;
                float t = Mathf.Approximately(minStrength, maxStrength)
                    ? 1f
                    : Mathf.InverseLerp(minStrength, maxStrength, ownershipStrengths[ownershipIndex]);
                float minAlpha = ownershipFlag == OwnershipBlack
                    ? OwnershipBlackPointMinAlpha
                    : OwnershipWhitePointMinAlpha;
                float maxAlpha = ownershipFlag == OwnershipBlack
                    ? OwnershipBlackPointMaxAlpha
                    : OwnershipWhitePointMaxAlpha;
                float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
                ownershipPoints[ownershipIndex] = new OwnershipPointDrawContext(ownershipFlag, alpha);
            }

            return ownershipPoints;
        }

        private void CreateOwnershipSquare(int x, int z, float squareSize, Material material, float alpha)
        {
            GameObject square = GameObject.CreatePrimitive(PrimitiveType.Quad);
            square.name = $"Ownership_{x}_{z}";
            square.transform.SetParent(ownershipRoot.transform, false);
            square.transform.localPosition = GetOwnershipLocalPosition(x, z, OwnershipYOffset);
            square.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            square.transform.localScale = new Vector3(squareSize, squareSize, 1f);

            RemoveOwnershipCollider(square);
            ApplyOwnershipMaterial(square, material, alpha);
        }

        private Vector3 GetOwnershipLocalPosition(int x, int z, float y)
        {
            Vector3 localPosition = GetCellCenterLocalPosition(x, z);
            localPosition.y = y;
            return localPosition;
        }

        private Material GetOwnershipMaterial(int ownershipFlag)
        {
            if (ownershipFlag == OwnershipBlack) {
                return GetBlackMaterial();
            }

            return GetWhiteMaterial();
        }

        private Material GetLatestMoveMarkerMaterial(bool isBlackStone)
        {
            return isBlackStone ? latestMoveMarkerOnBlackStoneMaterial : latestMoveMarkerOnWhiteStoneMaterial;
        }

        private Material GetBlackMaterial()
        {
            return blackMaterial;
        }

        private Material GetWhiteMaterial()
        {
            return whiteMaterial;
        }

        private Mesh GetLatestMoveMarkerMesh()
        {
            if (latestMoveMarkerMesh == null) {
                latestMoveMarkerMesh = CreateLatestMoveMarkerMesh(ChessBoardConfig.rectCellSideLength * LatestMoveMarkerSizeFactor);
            }

            return latestMoveMarkerMesh;
        }

        private Mesh GetAiRecommendationCircleMesh()
        {
            if (aiRecommendationCircleMesh == null) {
                aiRecommendationCircleMesh = CreateCircleMesh(
                    ChessBoardConfig.rectCellSideLength * AiRecommendationMarkerSizeFactor,
                    AiRecommendationCircleSegments);
            }

            return aiRecommendationCircleMesh;
        }

        private Mesh GetAiRecommendationCircleOutlineMesh()
        {
            if (aiRecommendationCircleOutlineMesh == null) {
                aiRecommendationCircleOutlineMesh = CreateRingMesh(
                    ChessBoardConfig.rectCellSideLength * AiRecommendationMarkerSizeFactor,
                    AiRecommendationOutlineWidth,
                    AiRecommendationCircleSegments);
            }

            return aiRecommendationCircleOutlineMesh;
        }

        private Material GetAiRecommendationCircleMaterial()
        {
            if (aiRecommendationCircleMaterial == null) {
                aiRecommendationCircleMaterial = CreateAiRecommendationMaterial(AiRecommendationColor);
            }

            return aiRecommendationCircleMaterial;
        }

        private Material GetAiRecommendationOutlineMaterial()
        {
            if (aiRecommendationOutlineMaterial == null) {
                aiRecommendationOutlineMaterial = CreateAiRecommendationMaterial(AiRecommendationOutlineColor);
            }

            return aiRecommendationOutlineMaterial;
        }

        private Material CreateAiRecommendationMaterial(Color markerColor)
        {
            Shader shader = GetAiRecommendationMaterialShader();
            if (shader == null) {
                return null;
            }

            Material material = new Material(shader);
            material.color = markerColor;
            if (material.HasProperty("_BaseColor")) {
                material.SetColor("_BaseColor", markerColor);
            }
            if (material.HasProperty("_Color")) {
                material.SetColor("_Color", Color.white);
            }
            return material;
        }

        private void ApplyAiRecommendationColor(MeshRenderer renderer, Material material, Color color)
        {
            if (renderer == null || material == null) {
                return;
            }

            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            if (material.HasProperty("_BaseColor")) {
                propertyBlock.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color")) {
                propertyBlock.SetColor("_Color", Color.white);
            }

            renderer.SetPropertyBlock(propertyBlock);
        }

        private Shader GetAiRecommendationMaterialShader()
        {
            return latestMoveMarkerOnBlackStoneMaterial?.shader
                ?? latestMoveMarkerOnWhiteStoneMaterial?.shader
                ?? blackMaterial?.shader
                ?? whiteMaterial?.shader;
        }

        private Color ResolveAiRecommendationColor(float alpha)
        {
            Color color = AiRecommendationColor;
            color.a = Mathf.Clamp(alpha, AiRecommendationLowestAlpha, AiRecommendationHighestAlpha);
            return color;
        }

        private List<RectGridAiRecommendationMarkerDrawContext> CreateAiRecommendationMarkerDrawContexts(
            List<RectGridAiRecommendationMarker> markers)
        {
            List<RectGridAiRecommendationMarkerDrawContext> markerContexts =
                new List<RectGridAiRecommendationMarkerDrawContext>();
            if (markers == null || markers.Count == 0) {
                return markerContexts;
            }

            List<int> sortedWinrates = new List<int>();
            foreach (RectGridAiRecommendationMarker marker in markers) {
                int winratePercent = Mathf.Clamp(marker.winratePercent, 1, 100);
                if (!sortedWinrates.Contains(winratePercent)) {
                    sortedWinrates.Add(winratePercent);
                }
            }

            sortedWinrates.Sort((left, right) => right.CompareTo(left));
            Dictionary<int, float> alphaByWinrate = new Dictionary<int, float>(sortedWinrates.Count);
            int denominator = sortedWinrates.Count - 1;
            for (int i = 0; i < sortedWinrates.Count; i++) {
                float t = denominator <= 0 ? 0f : (float)i / denominator;
                float alpha = Mathf.Lerp(AiRecommendationHighestAlpha, AiRecommendationLowestAlpha, t);
                alphaByWinrate[sortedWinrates[i]] = alpha;
            }

            foreach (RectGridAiRecommendationMarker marker in markers) {
                int winratePercent = Mathf.Clamp(marker.winratePercent, 1, 100);
                markerContexts.Add(new RectGridAiRecommendationMarkerDrawContext(
                    marker,
                    alphaByWinrate[winratePercent]));
            }

            return markerContexts;
        }

        private void ClearAiRecommendationMarkerMaterials()
        {
            if (aiRecommendationCircleMaterial != null) {
                Destroy(aiRecommendationCircleMaterial);
                aiRecommendationCircleMaterial = null;
            }

            if (aiRecommendationOutlineMaterial != null) {
                Destroy(aiRecommendationOutlineMaterial);
                aiRecommendationOutlineMaterial = null;
            }
        }

        private void OnDestroy()
        {
            ClearCoordinateLabels();
            ClearOwnership();
            ClearLatestMoveMarker();
            ClearMoveNumberMarkers();
            ClearAiRecommendationMarkers();
            ClearAiRecommendationMarkerMaterials();

            if (latestMoveMarkerMesh != null) {
                Destroy(latestMoveMarkerMesh);
                latestMoveMarkerMesh = null;
            }

            if (aiRecommendationCircleMesh != null) {
                Destroy(aiRecommendationCircleMesh);
                aiRecommendationCircleMesh = null;
            }

            if (aiRecommendationCircleOutlineMesh != null) {
                Destroy(aiRecommendationCircleOutlineMesh);
                aiRecommendationCircleOutlineMesh = null;
            }
        }

        private Mesh CreateLatestMoveMarkerMesh(float markerSize)
        {
            float halfWidth = markerSize * 0.5f;
            float halfHeight = markerSize * 0.5f;
            Mesh markerMesh = new Mesh();
            markerMesh.name = "LatestMoveMarkerMesh";
            markerMesh.SetVertices(new List<Vector3>
            {
                new Vector3(0f, 0f, halfHeight),
                new Vector3(-halfWidth, 0f, -halfHeight),
                new Vector3(halfWidth, 0f, -halfHeight),
            });
            markerMesh.SetTriangles(new[] { 0, 2, 1 }, 0);
            markerMesh.RecalculateNormals();
            return markerMesh;
        }

        private Mesh CreateCircleMesh(float diameter, int segmentCount)
        {
            float radius = diameter * 0.5f;
            int safeSegmentCount = Mathf.Max(segmentCount, 12);
            List<Vector3> vertices = new List<Vector3>(safeSegmentCount + 2)
            {
                Vector3.zero
            };
            List<int> triangles = new List<int>(safeSegmentCount * 3);

            for (int i = 0; i <= safeSegmentCount; i++) {
                float angle = Mathf.PI * 2f * i / safeSegmentCount;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            for (int i = 1; i <= safeSegmentCount; i++) {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }

            Mesh circleMesh = new Mesh();
            circleMesh.name = "AiRecommendationCircleMesh";
            circleMesh.SetVertices(vertices);
            circleMesh.SetTriangles(triangles, 0);
            circleMesh.RecalculateNormals();
            return circleMesh;
        }

        private Mesh CreateRingMesh(float outerDiameter, float lineWidth, int segmentCount)
        {
            float outerRadius = outerDiameter * 0.5f;
            float innerRadius = Mathf.Max(outerRadius - lineWidth, outerRadius * 0.5f);
            int safeSegmentCount = Mathf.Max(segmentCount, 12);
            List<Vector3> vertices = new List<Vector3>((safeSegmentCount + 1) * 2);
            List<int> triangles = new List<int>(safeSegmentCount * 6);

            for (int i = 0; i <= safeSegmentCount; i++) {
                float angle = Mathf.PI * 2f * i / safeSegmentCount;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                vertices.Add(new Vector3(cos * outerRadius, 0f, sin * outerRadius));
                vertices.Add(new Vector3(cos * innerRadius, 0f, sin * innerRadius));
            }

            for (int i = 0; i < safeSegmentCount; i++) {
                int outerA = i * 2;
                int innerA = outerA + 1;
                int outerB = outerA + 2;
                int innerB = outerA + 3;
                triangles.Add(outerA);
                triangles.Add(innerA);
                triangles.Add(outerB);
                triangles.Add(innerA);
                triangles.Add(innerB);
                triangles.Add(outerB);
            }

            Mesh ringMesh = new Mesh();
            ringMesh.name = "AiRecommendationCircleOutlineMesh";
            ringMesh.SetVertices(vertices);
            ringMesh.SetTriangles(triangles, 0);
            ringMesh.RecalculateNormals();
            return ringMesh;
        }

        private void RemoveOwnershipCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) {
                Destroy(collider);
            }
        }

        private void ApplyOwnershipMaterial(GameObject go, Material material, float alpha)
        {
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) {
                renderer.sharedMaterial = material;
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                float clampedAlpha = Mathf.Clamp01(alpha);
                if (material.HasProperty("_BaseColor")) {
                    Color baseColor = material.GetColor("_BaseColor");
                    baseColor.a = clampedAlpha;
                    propertyBlock.SetColor("_BaseColor", baseColor);
                }
                if (material.HasProperty("_Color")) {
                    Color legacyColor = material.HasProperty("_Color")
                        ? material.GetColor("_Color")
                        : Color.white;
                    legacyColor.a = 1f;
                    propertyBlock.SetColor("_Color", legacyColor);
                }

                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        // 检查cell是否位于整个棋盘的最外圈边界上
        public bool CheckCellOnEdge(RectCell cell)
        {
            if (cell?.coordinates == null) {
                XNLogger.LogError("Cell or coordinates is null, check cell edge failed.");
                return false;
            }

            int cellX = cell.coordinates.x;
            int cellZ = cell.coordinates.z;
            return cellX == 0 || cellX == gridSize - 1 || cellZ == 0 || cellZ == gridSize - 1;
        }

        private void CreateChunks()
        {
            chunkList.Clear();
            int chunkSize = ChessBoardConfig.chessBoardChunkSize;
            for (int startCellZ = 0; startCellZ < gridSize; startCellZ += chunkSize) {
                int curChunkSizeZ = Mathf.Min(chunkSize, gridSize - startCellZ);

                for (int startCellX = 0; startCellX < gridSize; startCellX += chunkSize) {
                    int curChunkSizeX = Mathf.Min(chunkSize, gridSize - startCellX);
                    GameObject chunkGO = Instantiate(chunkPrefab, transform);
                    chunkGO.name = $"RectGridChunk_{startCellX}_{startCellZ}";

                    RectGridChunk chunk = chunkGO.GetComponent<RectGridChunk>();
                    chunk.InitChunk(startCellX, startCellZ, curChunkSizeX, curChunkSizeZ, gridSize);
                    chunkList.Add(chunk);

                    chunk.SetDirty();
                }
            }
        }

        private void CreateCells()
        {
            int chunkSize = ChessBoardConfig.chessBoardChunkSize;
            int chunkCountX = Mathf.CeilToInt((float)gridSize / chunkSize);

            cellList.Clear();
            for (int cellZ = 0; cellZ < gridSize; cellZ++) {
                for (int cellX = 0; cellX < gridSize; cellX++) {
                    int chunkX = cellX / chunkSize;
                    int chunkZ = cellZ / chunkSize;
                    int chunkIndex = chunkZ * chunkCountX + chunkX;

                    if (chunkIndex < 0 || chunkIndex >= chunkList.Count) {
                        XNLogger.LogError(
                            "Chunk index out of range, add cell to chunk failed.",
                            ("cellX", cellX.ToString()),
                            ("cellZ", cellZ.ToString()),
                            ("chunkIndex", chunkIndex.ToString()),
                            ("chunkCount", chunkList.Count.ToString())
                        );
                        continue;
                    }

                    RectGridChunk ownerChunk = chunkList[chunkIndex];
                    RectCell cell = CreateCell(ownerChunk, cellX, cellZ);
                    cellList.Add(cell);
                    chunkList[chunkIndex].AddCellToChunk(cell);
                }
            }
        }

        private RectCell CreateCell(RectGridChunk ownerChunk, int x, int z)
        {
            RectCell cell = new RectCell(ownerChunk, new RectCoordinates(x, z));
            cell.isOnEdge = CheckCellOnEdge(cell);

            if (x > 0) {
                RectCell westNeighbor = cellList[cellList.Count - 1];
                cell.neighbors[(int)RectDirection.W] = westNeighbor;
                westNeighbor.neighbors[(int)RectDirection.E] = cell;
            }

            if (z > 0) {
                int northNeighborIndex = (z - 1) * gridSize + x;
                RectCell northNeighbor = cellList[northNeighborIndex];
                cell.neighbors[(int)RectDirection.N] = northNeighbor;
                northNeighbor.neighbors[(int)RectDirection.S] = cell;
            }

            return cell;
        }

        [ContextMenu("Debug Print All Cells")]
        public void DebugPrintAllCells()
        {
            var sb = new StringBuilder();
            sb.Append("RectGrid debug print all cells by chunk order.");

            for (int i = 0; i < chunkList.Count; i++) {
                RectGridChunk chunk = chunkList[i];
                if (chunk == null) {
                    sb.AppendLine();
                    sb.Append($"chunkIndex:{i} null");
                    continue;
                }

                sb.AppendLine();
                sb.Append($"chunkIndex:{i} startCell:({chunk.startCellX},{chunk.startCellZ}) size:({chunk.chunkSizeX},{chunk.chunkSizeZ})");
                sb.AppendLine();
                sb.Append(chunk.GetDebugCellLayout());
            }

            XNLogger.LogInfo(
                sb.ToString(),
                ("gridSize", gridSize.ToString()),
                ("chunkCount", chunkList.Count.ToString()),
                ("cellCount", cellList.Count.ToString())
            );
        }
    }
}
