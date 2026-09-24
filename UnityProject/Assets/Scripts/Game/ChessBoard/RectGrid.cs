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
        private const float CoordinateLabelOuterOffsetFactor = 0.45f;
        private const float CoordinateLabelFontSize = 20f;
        private const int CoordinateLabelSortingOrder = 1;
        // 坐标按印在木面上的墨字处理，比棋盘线略淡。
        private static readonly Color CoordinateLabelColor = new Color(BoardSurfaceMarker.Ink.r, BoardSurfaceMarker.Ink.g, BoardSurfaceMarker.Ink.b, 0.72f);

        private const float OwnershipSquareSizeFactor = ChessBoardConfig.starPointRadiusFactor * 2f * 2.5f;
        private const float OwnershipYOffset = 0.04f;
        private const float OwnershipBlackPointMinAlpha = 0.55f;
        private const float OwnershipBlackPointMaxAlpha = 0.95f;
        private const float OwnershipWhitePointMinAlpha = 0.45f;
        private const float OwnershipWhitePointMaxAlpha = 0.85f;
        private const float LatestMoveMarkerYOffset = 0.05f;
        private const float MoveNumberMarkerYOffset = 1.74f;
        private const float AiRecommendationDiscYOffset = 0.10f;
        private const float AiRecommendationTextYOffset = 0.115f;
        // 推荐圆片与棋子等大，读起来像“这里该落一子”。
        private const float AiRecommendationDiscSizeFactor = 0.86f;
        private const float AiRecommendationOutlineWidth = 0.06f;
        private const float AiRecommendationFontSize = 16f;
        private const int AiRecommendationDiscSortingOrder = 28;
        private const int AiRecommendationTextSortingOrder = 30;
        private const int OwnershipNeutral = 0;
        private const int OwnershipBlack = 1;
        private const int OwnershipWhite = -1;
        // 按胜率名次分档：第一推荐完全不透明，名次越低越淡（透出网格线正好表示“弱”），区间要够宽才能一眼分出高低。
        private const float AiRecommendationLowestAlpha = 0.4f;
        private const float AiRecommendationHighestAlpha = 1f;
        // 亮绿圆片表示“好点” + 深一档的细描边；亮底上纸色字对比不足，胜率用墨色。
        private static readonly Color AiRecommendationColor = new Color32(0x3C, 0xB9, 0x5A, 0xFF);
        private static readonly Color AiRecommendationOutlineColor = new Color32(0x25, 0x84, 0x3F, 0xFF);
        private static readonly Color AiRecommendationTextColor = new Color(BoardSurfaceMarker.Ink.r, BoardSurfaceMarker.Ink.g, BoardSurfaceMarker.Ink.b, 0.9f);
        private static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorShaderId = Shader.PropertyToID("_Color");
        private static readonly int ShapeShaderId = Shader.PropertyToID("_Shape");
        private static readonly int OutlineColorShaderId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthShaderId = Shader.PropertyToID("_OutlineWidth");
        private const float DiscShape = 1f;
        private const int BoardTextureReferenceGridSize = 19;
        private static readonly int BoardGridShaderId = Shader.PropertyToID("_XNBoardGrid");
        private static readonly int BoardStarShaderId = Shader.PropertyToID("_XNBoardStar");
        private static readonly int BoardRectShaderId = Shader.PropertyToID("_XNBoardRect");
        private static readonly int BoardUVShaderId = Shader.PropertyToID("_XNBoardUV");
        private static readonly int BoardWorldToGridShaderId = Shader.PropertyToID("_XNBoardWorldToGrid");

        private Material blackMaterial;
        private Material whiteMaterial;
        private Material latestMoveMarkerOnBlackStoneMaterial;
        private Material latestMoveMarkerOnWhiteStoneMaterial;
        private Material aiRecommendationDiscMaterial;

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

            Material material = GetLatestMoveMarkerMaterial(isBlackStone);
            if (material == null) {
                return;
            }

            latestMoveMarkerRoot = new GameObject("LatestMoveMarkerRoot");
            latestMoveMarkerRoot.transform.SetParent(transform, false);
            BoardSurfaceMarker.CreateLatestMoveMarker(
                latestMoveMarkerRoot.transform,
                $"LatestMoveMarker_{x}_{z}",
                GetOwnershipLocalPosition(x, z, LatestMoveMarkerYOffset),
                material);
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

            float cellSize = ChessBoardConfig.rectCellSideLength;
            float boardSideLength = gridSize * cellSize;
            float boardCenter = boardSideLength / 2f;
            float labelOuterOffset = cellSize *
                ChessBoardVisualConfig.boardOuterBorderWidthFactor *
                CoordinateLabelOuterOffsetFactor;

            // 每条边只用一个 TMP 文本：字母行按格宽等距排布、数字列按格宽固定行高，居中后逐字落在对应网格线上。
            string cellEm = BoardSurfaceMarker.ToEm(cellSize, CoordinateLabelFontSize);
            StringBuilder columnLabels = new StringBuilder($"<mspace={cellEm}>");
            StringBuilder rowLabels = new StringBuilder($"<line-height={cellEm}>");
            for (int i = 0; i < gridSize; i++) {
                columnLabels.Append(GetGoCoordinateColumnLabel(i));
                if (i > 0) {
                    rowLabels.Append('\n');
                }
                rowLabels.Append(gridSize - i);
            }

            string columnText = columnLabels.ToString();
            string rowText = rowLabels.ToString();
            float y = CoordinateLabelSurfaceYOffset;
            CreateCoordinateLabel("CoordinateTop", columnText, new Vector3(boardCenter, y, boardSideLength + labelOuterOffset));
            CreateCoordinateLabel("CoordinateBottom", columnText, new Vector3(boardCenter, y, -labelOuterOffset));
            CreateCoordinateLabel("CoordinateLeft", rowText, new Vector3(-labelOuterOffset, y, boardCenter));
            CreateCoordinateLabel("CoordinateRight", rowText, new Vector3(boardSideLength + labelOuterOffset, y, boardCenter));
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
            RefreshBoardShaderGlobals();
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

        // Ground.shader 按棋盘坐标解析绘制木纹、网格线、星位和盘边倒角，这里同步棋盘几何参数。
        private void RefreshBoardShaderGlobals()
        {
            float cellSize = ChessBoardConfig.rectCellSideLength;
            float sideLength = gridSize * cellSize;
            float borderWidth = cellSize * ChessBoardVisualConfig.boardOuterBorderWidthFactor;
            float outerPadding = boardCoordinateFrameVisible ? borderWidth : 0f;
            // 木纹按固定物理尺寸铺设，小路数棋盘只取贴图中央部分，纹理疏密与 19 路一致。
            float textureSize = Mathf.Max(BoardTextureReferenceGridSize, gridSize) * cellSize + borderWidth * 2f;
            float textureOrigin = sideLength / 2f - textureSize / 2f;

            Vector4 starLayout = Vector4.zero;
            if (ChessBoardUtils.TryGetStarPointLayout(gridSize, out int low, out int mid, out int high, out bool onlyCornersAndCenter)) {
                starLayout = new Vector4(low, mid, high, onlyCornersAndCenter ? 1f : 2f);
            }

            Shader.SetGlobalVector(BoardGridShaderId, new Vector4(gridSize, cellSize, 0f, 0f));
            Shader.SetGlobalVector(BoardStarShaderId, starLayout);
            Shader.SetGlobalVector(BoardRectShaderId, new Vector4(-outerPadding, -outerPadding, sideLength + outerPadding, sideLength + outerPadding));
            Shader.SetGlobalVector(BoardUVShaderId, new Vector4(textureOrigin, textureOrigin, 1f / textureSize, 1f / textureSize));
            Shader.SetGlobalMatrix(BoardWorldToGridShaderId, transform.worldToLocalMatrix);
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

            BoardSurfaceMarker.CreateLabel(
                coordinateLabelRoot.transform,
                objectName,
                labelText,
                localPosition,
                CoordinateLabelFontSize,
                CoordinateLabelColor,
                CoordinateLabelSortingOrder);
        }

        private void CreateMoveNumberMarker(RectGridMoveNumberMarker marker)
        {
            BoardSurfaceMarker.CreateMoveNumber(
                moveNumberMarkerRoot.transform,
                $"MoveNumber_{marker.moveNumber}_{marker.x}_{marker.z}",
                marker.moveNumber,
                marker.isBlackStone,
                GetOwnershipLocalPosition(marker.x, marker.z, MoveNumberMarkerYOffset));
        }

        private void CreateAiRecommendationMarker(RectGridAiRecommendationMarkerDrawContext markerContext)
        {
            Material material = GetAiRecommendationDiscMaterial();
            if (material == null) {
                return;
            }

            RectGridAiRecommendationMarker marker = markerContext.marker;
            MeshRenderer disc = BoardSurfaceMarker.CreateQuad(
                aiRecommendationMarkerRoot.transform,
                $"AiRecommendationDisc_{marker.order}_{marker.x}_{marker.z}",
                GetOwnershipLocalPosition(marker.x, marker.z, AiRecommendationDiscYOffset),
                ChessBoardConfig.rectCellSideLength * AiRecommendationDiscSizeFactor,
                material,
                AiRecommendationDiscSortingOrder);
            ApplyAiRecommendationColor(disc, ResolveAiRecommendationColor(markerContext.alpha));

            int winratePercent = Mathf.Clamp(marker.winratePercent, 1, 100);
            BoardSurfaceMarker.CreateLabel(
                aiRecommendationMarkerRoot.transform,
                $"AiRecommendationText_{marker.order}_{marker.x}_{marker.z}",
                winratePercent.ToString(),
                GetOwnershipLocalPosition(marker.x, marker.z, AiRecommendationTextYOffset),
                BoardSurfaceMarker.ResolveNumberFontSize(winratePercent, AiRecommendationFontSize),
                AiRecommendationTextColor,
                AiRecommendationTextSortingOrder);
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
            MeshRenderer square = BoardSurfaceMarker.CreateQuad(
                ownershipRoot.transform,
                $"Ownership_{x}_{z}",
                GetOwnershipLocalPosition(x, z, OwnershipYOffset),
                squareSize,
                material,
                0);
            ApplyOwnershipAlpha(square, material, alpha);
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

        // 推荐圆片与最后一手标记共用 BoardOverlay shader，运行时按圆片形状和描边单独建一份材质。
        private Material GetAiRecommendationDiscMaterial()
        {
            if (aiRecommendationDiscMaterial != null) {
                return aiRecommendationDiscMaterial;
            }

            Shader shader = GetAiRecommendationMaterialShader();
            if (shader == null) {
                return null;
            }

            aiRecommendationDiscMaterial = new Material(shader);
            aiRecommendationDiscMaterial.SetColor(BaseColorShaderId, AiRecommendationColor);
            aiRecommendationDiscMaterial.SetColor(LegacyColorShaderId, Color.white);
            aiRecommendationDiscMaterial.SetFloat(ShapeShaderId, DiscShape);
            aiRecommendationDiscMaterial.SetColor(OutlineColorShaderId, AiRecommendationOutlineColor);
            aiRecommendationDiscMaterial.SetFloat(OutlineWidthShaderId, AiRecommendationOutlineWidth);
            return aiRecommendationDiscMaterial;
        }

        private void ApplyAiRecommendationColor(MeshRenderer renderer, Color color)
        {
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor(BaseColorShaderId, color);
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
            if (aiRecommendationDiscMaterial != null) {
                Destroy(aiRecommendationDiscMaterial);
                aiRecommendationDiscMaterial = null;
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
        }

        private void ApplyOwnershipAlpha(MeshRenderer renderer, Material material, float alpha)
        {
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            if (material.HasProperty(BaseColorShaderId)) {
                Color baseColor = material.GetColor(BaseColorShaderId);
                baseColor.a = Mathf.Clamp01(alpha);
                propertyBlock.SetColor(BaseColorShaderId, baseColor);
            }
            if (material.HasProperty(LegacyColorShaderId)) {
                Color legacyColor = material.GetColor(LegacyColorShaderId);
                legacyColor.a = 1f;
                propertyBlock.SetColor(LegacyColorShaderId, legacyColor);
            }

            renderer.SetPropertyBlock(propertyBlock);
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
