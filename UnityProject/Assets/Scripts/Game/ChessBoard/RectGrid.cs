using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.Logger;

namespace XNClient.ChessBoard
{
    public class RectGrid : MonoBehaviour
    {
        public GameObject chunkPrefab;
        public int gridSize;
        private List<RectGridChunk> chunkList = new List<RectGridChunk>();
        private List<RectCell> cellList = new List<RectCell>();
        private GameObject ownershipRoot;
        private GameObject latestMoveMarkerRoot;

        private const float OwnershipSquareSizeFactor = ChessBoardConfig.starPointRadiusFactor * 2f * 1.5f;
        private const float OwnershipLineWidthFactor = ChessBoardConfig.roadNormalFactor * 1.5f;
        private const float OwnershipYOffset = ChessBoardConfig.rectCellSideLength;
        private const float OwnershipLineYOffset = OwnershipYOffset - 0.01f;
        private const float LatestMoveMarkerSizeFactor = ChessBoardConfig.starPointRadiusFactor * 2f * 2.3f;
        private const int OwnershipNeutral = 0;
        private const int OwnershipBlack = 1;
        private const int OwnershipWhite = -1;

        private Material ownershipBlackMaterial;
        private Material ownershipWhiteMaterial;
        private Material latestMoveMarkerBlackMaterial;
        private Material latestMoveMarkerWhiteMaterial;
        private Mesh latestMoveMarkerMesh;

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
        }

        public Bounds GetGridBounds()
        {
            float gridSideLength = gridSize * ChessBoardConfig.rectCellSideLength;
            Vector3 localCenter = new Vector3(gridSideLength / 2f, 0f, gridSideLength / 2f);
            Vector3 worldCenter = transform.TransformPoint(localCenter);
            Vector3 size = new Vector3(gridSideLength, 0f, gridSideLength);
            return new Bounds(worldCenter, size);
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

            int[] ownershipFlags = BuildOwnershipFlags(ownership, ownershipThreshold, expectedCount);
            DrawOwnershipLines(ownershipFlags);

            float squareSize = ChessBoardConfig.rectCellSideLength * OwnershipSquareSizeFactor;
            for (int z = 0; z < gridSize; z++) {
                for (int x = 0; x < gridSize; x++) {
                    int ownershipFlag = ownershipFlags[z * gridSize + x];
                    if (ownershipFlag == OwnershipNeutral) {
                        continue;
                    }

                    CreateOwnershipSquare(x, z, squareSize, GetOwnershipMaterial(ownershipFlag));
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
            marker.transform.localPosition = GetOwnershipLocalPosition(x, z, OwnershipYOffset);

            MeshFilter meshFilter = marker.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetLatestMoveMarkerMesh();

            MeshRenderer meshRenderer = marker.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetLatestMoveMarkerMaterial(isBlackStone);
        }

        public void ClearLatestMoveMarker()
        {
            if (latestMoveMarkerRoot == null) {
                return;
            }

            Destroy(latestMoveMarkerRoot);
            latestMoveMarkerRoot = null;
        }

        private int[] BuildOwnershipFlags(JArray ownership, float ownershipThreshold, int expectedCount)
        {
            int[] ownershipFlags = new int[expectedCount];
            for (int ownershipIndex = 0; ownershipIndex < expectedCount; ownershipIndex++) {
                if (!float.TryParse(ownership[ownershipIndex]?.ToString(), out float ownershipValue)) {
                    continue;
                }

                if (Mathf.Abs(ownershipValue) <= ownershipThreshold) {
                    continue;
                }

                ownershipFlags[ownershipIndex] = ownershipValue > 0f ? OwnershipBlack : OwnershipWhite;
            }

            return ownershipFlags;
        }

        private void DrawOwnershipLines(int[] ownershipFlags)
        {
            float lineWidth = ChessBoardConfig.rectCellSideLength * OwnershipLineWidthFactor;
            float lineLength = ChessBoardConfig.rectCellSideLength;
            for (int z = 0; z < gridSize; z++) {
                for (int x = 0; x < gridSize; x++) {
                    int ownershipFlag = ownershipFlags[z * gridSize + x];
                    if (ownershipFlag == OwnershipNeutral) {
                        continue;
                    }

                    if (x + 1 < gridSize && ownershipFlags[z * gridSize + x + 1] == ownershipFlag) {
                        Vector3 centerA = GetOwnershipLocalPosition(x, z, OwnershipLineYOffset);
                        Vector3 centerB = GetOwnershipLocalPosition(x + 1, z, OwnershipLineYOffset);
                        CreateOwnershipLine(centerA, centerB, lineLength, lineWidth, true, GetOwnershipMaterial(ownershipFlag));
                    }

                    if (z + 1 < gridSize && ownershipFlags[(z + 1) * gridSize + x] == ownershipFlag) {
                        Vector3 centerA = GetOwnershipLocalPosition(x, z, OwnershipLineYOffset);
                        Vector3 centerB = GetOwnershipLocalPosition(x, z + 1, OwnershipLineYOffset);
                        CreateOwnershipLine(centerA, centerB, lineLength, lineWidth, false, GetOwnershipMaterial(ownershipFlag));
                    }
                }
            }
        }

        private void CreateOwnershipSquare(int x, int z, float squareSize, Material material)
        {
            GameObject square = GameObject.CreatePrimitive(PrimitiveType.Quad);
            square.name = $"Ownership_{x}_{z}";
            square.transform.SetParent(ownershipRoot.transform, false);
            square.transform.localPosition = GetOwnershipLocalPosition(x, z, OwnershipYOffset);
            square.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            square.transform.localScale = new Vector3(squareSize, squareSize, 1f);

            RemoveOwnershipCollider(square);
            ApplyOwnershipMaterial(square, material);
        }

        private void CreateOwnershipLine(Vector3 centerA, Vector3 centerB, float lineLength, float lineWidth, bool isHorizontal, Material material)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Quad);
            line.name = isHorizontal ? "OwnershipLine_H" : "OwnershipLine_V";
            line.transform.SetParent(ownershipRoot.transform, false);
            line.transform.localPosition = (centerA + centerB) * 0.5f;
            line.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            line.transform.localScale = isHorizontal
                ? new Vector3(lineLength, lineWidth, 1f)
                : new Vector3(lineWidth, lineLength, 1f);

            RemoveOwnershipCollider(line);
            ApplyOwnershipMaterial(line, material);
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
                if (ownershipBlackMaterial == null) {
                    ownershipBlackMaterial = CreateOwnershipMaterial(Color.black);
                }
                return ownershipBlackMaterial;
            }

            if (ownershipWhiteMaterial == null) {
                ownershipWhiteMaterial = CreateOwnershipMaterial(Color.white);
            }
            return ownershipWhiteMaterial;
        }

        private Material GetLatestMoveMarkerMaterial(bool isBlackStone)
        {
            if (isBlackStone) {
                if (latestMoveMarkerWhiteMaterial == null) {
                    latestMoveMarkerWhiteMaterial = CreateOwnershipMaterial(Color.white);
                }
                return latestMoveMarkerWhiteMaterial;
            }

            if (latestMoveMarkerBlackMaterial == null) {
                latestMoveMarkerBlackMaterial = CreateOwnershipMaterial(Color.black);
            }
            return latestMoveMarkerBlackMaterial;
        }

        private Mesh GetLatestMoveMarkerMesh()
        {
            if (latestMoveMarkerMesh == null) {
                latestMoveMarkerMesh = CreateLatestMoveMarkerMesh(ChessBoardConfig.rectCellSideLength * LatestMoveMarkerSizeFactor);
            }

            return latestMoveMarkerMesh;
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

        private Material CreateOwnershipMaterial(Color color)
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = color;
            return material;
        }

        private void RemoveOwnershipCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) {
                Destroy(collider);
            }
        }

        private void ApplyOwnershipMaterial(GameObject go, Material material)
        {
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) {
                renderer.sharedMaterial = material;
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
