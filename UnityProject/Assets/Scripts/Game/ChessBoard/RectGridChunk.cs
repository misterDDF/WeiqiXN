using System.Collections.Generic;
using UnityEngine;
using XNClient.Logger;

namespace XNClient.ChessBoard
{
    public class RectGridChunk : MonoBehaviour
    {
        public int startCellX, startCellZ;
        public int chunkSizeX, chunkSizeZ;
        public int gridSize;
        public RectMesh groundMesh, roadMesh;

        private List<RectCell> cellList = new List<RectCell>();
        private bool isDirty;

        // 固定通道语义：r=self，g=pointNeighbor.prev，b=pointNeighbor，a=pointNeighbor.next
        private static Color color1 = new Color(1f, 0f, 0f, 0f);
        private static Color color2 = new Color(0f, 1f, 0f, 0f);
        private static Color color3 = new Color(0f, 0f, 1f, 0f);
        private static Color color4 = new Color(0f, 0f, 0f, 1f);

        private void LateUpdate()
        {
            if (isDirty) {
                TriangulateChunk();
                isDirty = false;
            }
        }

        public void InitChunk(int startCellX, int startCellZ, int chunkSizeX, int chunkSizeZ, int gridSize)
        {
            if (startCellX < 0 || startCellZ < 0) {
                XNLogger.LogError("Chunk start cell should be positive, init chunk failed.", ("startCellX", startCellX.ToString()), ("startCellZ", startCellZ.ToString()));
                return;
            }
            this.startCellX = startCellX;
            this.startCellZ = startCellZ;

            if (chunkSizeX <= 0 || chunkSizeZ <= 0) {
                XNLogger.LogError($"Chunk size shoud be positive, init chunk failed.", ("chunkSizeX", chunkSizeX.ToString()), ("chunkSizeZ", chunkSizeZ.ToString()));
                return;
            }
            this.chunkSizeX = chunkSizeX;
            this.chunkSizeZ = chunkSizeZ;

            if (gridSize <= 0) {
                XNLogger.LogError("Grid size should be positive, init chunk failed.", ("gridSize", gridSize.ToString()));
                return;
            }
            this.gridSize = gridSize;

            transform.localPosition = new Vector3(
                startCellX * ChessBoardConfig.rectCellSideLength,
                0f,
                (gridSize - startCellZ - chunkSizeZ) * ChessBoardConfig.rectCellSideLength
            );

            cellList.Clear();
        }

        public void AddCellToChunk(RectCell cell)
        {
            if (cell == null || cell.chunk == null || cell.coordinates == null) {
                XNLogger.LogError("Cell, owner or coordinates is null, add cell to chunk failed.");
                return;
            }

            if (cell.chunk != this) {
                XNLogger.LogError(
                    "Cell owner does not match chunk, add cell to chunk failed.",
                    ("cellOwner", cell.chunk.name),
                    ("chunkName", name)
                );
                return;
            }

            int cellX = cell.coordinates.x;
            int cellZ = cell.coordinates.z;
            int minX = startCellX;
            int maxX = startCellX + chunkSizeX;
            int minZ = startCellZ;
            int maxZ = startCellZ + chunkSizeZ;

            if (cellX < minX || cellX >= maxX || cellZ < minZ || cellZ >= maxZ) {
                XNLogger.LogError(
                    "Cell coordinates out of chunk range, add cell to chunk failed.",
                    ("cellX", cellX.ToString()),
                    ("cellZ", cellZ.ToString()),
                    ("startCellX", startCellX.ToString()),
                    ("startCellZ", startCellZ.ToString()),
                    ("chunkSizeX", chunkSizeX.ToString()),
                    ("chunkSizeZ", chunkSizeZ.ToString())
                );
                return;
            }

            cellList.Add(cell);
            isDirty = true;
        }

        public void SetDirty()
        {
            isDirty = true;
        }

        private void TriangulateChunk()
        {
            groundMesh.ClearMesh();
            roadMesh.ClearMesh();

            foreach (RectCell cell in cellList) {
                TriangulateCell(cell);
            }
            TriangulateStarPoints();
            groundMesh.RefreshMesh();
            roadMesh.RefreshMesh();
        }

        private void TriangulateCell(RectCell cell)
        {
            // 将方格拆分为东南西北四个方向进行构建
            foreach (RectDirection dir in ChessBoardUtils.GetAllRectDirections()) {
                TriangulateCellByDirection(cell, dir);
            }
        }

        private void TriangulateStarPoints()
        {
            foreach (RectCell cell in cellList) {
                if (cell?.coordinates == null) {
                    continue;
                }
                if (!ChessBoardUtils.CheckIsStarPoint(cell.coordinates.x, cell.coordinates.z, gridSize)) {
                    continue;
                }

                TriangulateStarPoint(cell.centerPosInChunk);
            }
        }

        private void TriangulateStarPoint(Vector3 center)
        {
            int segmentCount = ChessBoardConfig.starPointSegmentCount;
            float radius = ChessBoardConfig.rectCellSideLength * ChessBoardConfig.starPointRadiusFactor;
            Vector3 starCenter = center + Vector3.up * ChessBoardConfig.starPointYOffset;
            Vector4 uv = new Vector4(1f, 0f, 0f, 0f);

            for (int i = 0; i < segmentCount; i++) {
                float angle1 = Mathf.PI * 2f * i / segmentCount;
                float angle2 = Mathf.PI * 2f * (i + 1) / segmentCount;
                Vector3 p1 = starCenter + new Vector3(Mathf.Cos(angle1) * radius, 0f, Mathf.Sin(angle1) * radius);
                Vector3 p2 = starCenter + new Vector3(Mathf.Cos(angle2) * radius, 0f, Mathf.Sin(angle2) * radius);

                roadMesh.AddTriangle(starCenter, p2, p1);
                roadMesh.AddTriangleUV0(uv);
            }
        }

        private void TriangulateCellByDirection(RectCell cell, RectDirection dir)
        {
            TriangulateCellInner(cell, dir);
            TriangulateCellOuter(cell, dir);
            TriangulateCellRoad(cell, dir);
        }

        // 内部纯色三角
        private void TriangulateCellInner(RectCell cell, RectDirection dir)
        {
            (Vector3, Vector3) edgeCornerOffsets = ChessBoardUtils.GetInnerCornerOffsets(dir);
            groundMesh.AddTriangle(
                cell.centerPosInChunk,
                cell.centerPosInChunk + edgeCornerOffsets.Item1,
                cell.centerPosInChunk + edgeCornerOffsets.Item2
            );
            groundMesh.AddTriangleColor(color1);
            groundMesh.AddTriangleUV0(GetSingleTextureIndices(cell));
        }

        // 外侧混色梯形
        private void TriangulateCellOuter(RectCell cell, RectDirection dir)
        {
            (Vector3, Vector3) innerCornerOffsets = ChessBoardUtils.GetInnerCornerOffsets(dir);
            (Vector3, Vector3) blendCornerOffsets = ChessBoardUtils.GetBlendCornerOffsets(dir);
            (Vector3, Vector3) outerCornerOffsets = ChessBoardUtils.GetOuterCornerOffsets(dir);
            Vector3 innerCorner1 = cell.centerPosInChunk + innerCornerOffsets.Item1;
            Vector3 innerCorner2 = cell.centerPosInChunk + innerCornerOffsets.Item2;
            Vector3 innerMid = (innerCorner1 + innerCorner2) / 2f;
            Vector3 outerCorner1 = cell.centerPosInChunk + outerCornerOffsets.Item1;
            Vector3 outerCorner2 = cell.centerPosInChunk + outerCornerOffsets.Item2;
            Vector3 blendCorner1 = cell.centerPosInChunk + blendCornerOffsets.Item1;
            Vector3 blendCorner2 = cell.centerPosInChunk + blendCornerOffsets.Item2;
            Vector3 blendMid = (blendCorner1 + blendCorner2) / 2f;

            Color lineMidColor1 = GetRelativeLineMidColor1(cell, dir);
            Color lineMidColor2 = GetRelativeLineMidColor2(cell, dir);
            Color pointColor1 = GetRelativeOuterPointColor1(cell, dir);
            Color pointColor2 = GetRelativeOuterPointColor2(cell, dir);
            Color cellColor = color1;
            Color edgeLerpColor1 = Color.Lerp(pointColor1, lineMidColor1, ChessBoardConfig.blendFactor);
            Color edgeLerpColor2 = Color.Lerp(pointColor2, lineMidColor2, ChessBoardConfig.blendFactor);
            Vector4 pointTextureIndices1 = GetRelativeOuterPointTextureIndices1(cell, dir);
            Vector4 pointTextureIndices2 = GetRelativeOuterPointTextureIndices2(cell, dir);

            // 中部过渡区域拆成左右两个小 quad，分别锚定两个角点邻居
            groundMesh.AddQuad(
                innerCorner1,
                innerMid,
                blendCorner1,
                blendMid
            );
            groundMesh.AddQuadColor(
                cellColor,
                cellColor,
                edgeLerpColor1,
                lineMidColor1
            );
            groundMesh.AddQuadUV0(pointTextureIndices1);

            groundMesh.AddQuad(
                innerMid,
                innerCorner2,
                blendMid,
                blendCorner2
            );
            groundMesh.AddQuadColor(
                cellColor,
                cellColor,
                lineMidColor2,
                edgeLerpColor2
            );
            groundMesh.AddQuadUV0(pointTextureIndices2);

            groundMesh.AddTriangle(
                innerCorner1,
                outerCorner1,
                blendCorner1
            );
            groundMesh.AddTriangleColor(
                cellColor,
                pointColor1,
                edgeLerpColor1
            );
            groundMesh.AddTriangleUV0(pointTextureIndices1);

            groundMesh.AddTriangle(
                innerCorner2,
                blendCorner2,
                outerCorner2
            );
            groundMesh.AddTriangleColor(
                cellColor,
                edgeLerpColor2,
                pointColor2
            );
            groundMesh.AddTriangleUV0(pointTextureIndices2);
        }

        // 左半边中点颜色，按 self -> point(dir).next 的固定通道语义混合
        private Color GetRelativeLineMidColor1(RectCell cell, RectDirection dir)
        {
            Color relativeColor = color1;
            float colorCount = 1f;
            if (cell.TryGetLineNeighbor(dir, out _)) {
                relativeColor += color4;
                colorCount += 1f;
            }

            return relativeColor / colorCount;
        }

        // 右半边中点颜色，按 self -> point(nextDir).prev 的固定通道语义混合
        private Color GetRelativeLineMidColor2(RectCell cell, RectDirection dir)
        {
            Color relativeColor = color1;
            float colorCount = 1f;
            if (cell.TryGetLineNeighbor(dir, out _)) {
                relativeColor += color2;
                colorCount += 1f;
            }

            return relativeColor / colorCount;
        }

        // 获取当前方向第一个外角的相对颜色，固定通道语义为 self -> prevDir -> point(dir) -> dir
        private Color GetRelativeOuterPointColor1(RectCell cell, RectDirection dir)
        {
            RectDirection prevDir = dir.GetPrevDirection();
            Color relativeColor = color1;
            float colorCount = 1f;

            if (cell.TryGetLineNeighbor(prevDir, out _)) {
                relativeColor += color2;
                colorCount += 1f;
            }
            if (cell.TryGetPointNeighbor(dir, out _)) {
                relativeColor += color3;
                colorCount += 1f;
            }
            if (cell.TryGetLineNeighbor(dir, out _)) {
                relativeColor += color4;
                colorCount += 1f;
            }

            return relativeColor / colorCount;
        }

        // 获取当前方向第二个外角的相对颜色，固定通道语义为 self -> dir -> point(nextDir) -> nextDir
        private Color GetRelativeOuterPointColor2(RectCell cell, RectDirection dir)
        {
            Color relativeColor = color1;
            float colorCount = 1f;
            RectDirection nextDir = dir.GetNextDirection();

            if (cell.TryGetLineNeighbor(dir, out _)) {
                relativeColor += color2;
                colorCount += 1f;
            }
            if (cell.TryGetPointNeighbor(nextDir, out _)) {
                relativeColor += color3;
                colorCount += 1f;
            }
            if (cell.TryGetLineNeighbor(nextDir, out _)) {
                relativeColor += color4;
                colorCount += 1f;
            }

            return relativeColor / colorCount;
        }

        private Vector4 GetSingleTextureIndices(RectCell cell)
        {
            return new Vector4(cell.textureIndex, cell.textureIndex, cell.textureIndex, cell.textureIndex);
        }

        // 获取当前方向第一个外角的贴图索引，按 self -> prevDir -> point(dir) -> dir 的通道顺序写入
        private Vector4 GetRelativeOuterPointTextureIndices1(RectCell cell, RectDirection dir)
        {
            int selfTextureIndex = cell.textureIndex;
            int prevLineTextureIndex = selfTextureIndex;
            int pointTextureIndex = selfTextureIndex;
            int lineTextureIndex = selfTextureIndex;
            RectDirection prevDir = dir.GetPrevDirection();

            if (cell.TryGetLineNeighbor(prevDir, out RectCell prevLineNeighbor)) {
                prevLineTextureIndex = prevLineNeighbor.textureIndex;
            }
            if (cell.TryGetPointNeighbor(dir, out RectCell pointNeighbor)) {
                pointTextureIndex = pointNeighbor.textureIndex;
            }
            if (cell.TryGetLineNeighbor(dir, out RectCell lineNeighbor)) {
                lineTextureIndex = lineNeighbor.textureIndex;
            }

            return new Vector4(selfTextureIndex, prevLineTextureIndex, pointTextureIndex, lineTextureIndex);
        }

        // 获取当前方向第二个外角的贴图索引，按 self -> dir -> point(nextDir) -> nextDir 的通道顺序写入
        private Vector4 GetRelativeOuterPointTextureIndices2(RectCell cell, RectDirection dir)
        {
            int selfTextureIndex = cell.textureIndex;
            int lineTextureIndex = selfTextureIndex;
            int pointTextureIndex = selfTextureIndex;
            int nextLineTextureIndex = selfTextureIndex;
            RectDirection nextDir = dir.GetNextDirection();

            if (cell.TryGetLineNeighbor(dir, out RectCell lineNeighbor)) {
                lineTextureIndex = lineNeighbor.textureIndex;
            }
            if (cell.TryGetPointNeighbor(nextDir, out RectCell pointNeighbor)) {
                pointTextureIndex = pointNeighbor.textureIndex;
            }
            if (cell.TryGetLineNeighbor(nextDir, out RectCell nextLineNeighbor)) {
                nextLineTextureIndex = nextLineNeighbor.textureIndex;
            }

            return new Vector4(selfTextureIndex, lineTextureIndex, pointTextureIndex, nextLineTextureIndex);
        }

        private void TriangulateCellRoad(RectCell cell, RectDirection dir)
        {
            // 道路内侧小三角，按中线切分为两个
            bool isOnBoardEdgeRoad = ChessBoardUtils.CheckRoadOnBoardEdge(cell.coordinates.x, cell.coordinates.z, gridSize, dir);
            (Vector3, Vector3) centerOffset = ChessBoardUtils.GetRoadCenterCornerOffsets(dir, isOnBoardEdgeRoad);
            Vector3 endPoint1 = cell.centerPosInChunk + centerOffset.Item1;
            Vector3 endPoint2 = cell.centerPosInChunk + centerOffset.Item2;
            Vector3 midPoint = (endPoint1 + endPoint2) / 2f;
            roadMesh.AddTriangle(
                cell.centerPosInChunk,
                endPoint1,
                midPoint
            );
            roadMesh.AddTriangleUV0(
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f)
            );
            roadMesh.AddTriangle(
                cell.centerPosInChunk,
                midPoint,
                endPoint2
            );
            roadMesh.AddTriangleUV0(
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f)
            );

            // 道路主体矩形，按中线切分为两份
            if (cell.TryGetLineNeighbor(dir, out RectCell neighbor)) {
                (Vector3, Vector3) innerOffset = ChessBoardUtils.GetRoadInnerCornerOffsets(dir, cell.isOnEdge, neighbor.isOnEdge);
                (Vector3, Vector3) outerOffset = ChessBoardUtils.GetRoadOuterCornerOffsets(dir, cell.isOnEdge, neighbor.isOnEdge);
                Vector3 innerEndPoint1 = cell.centerPosInChunk + innerOffset.Item1;
                Vector3 innerEndPoint2 = cell.centerPosInChunk + innerOffset.Item2;
                Vector3 innerMidPoint = (innerEndPoint1 + innerEndPoint2) / 2f;
                Vector3 outerEndPoint1 = cell.centerPosInChunk + outerOffset.Item1;
                Vector3 outerEndPoint2 = cell.centerPosInChunk + outerOffset.Item2;
                Vector3 outerMidPoint = (outerEndPoint1 + outerEndPoint2) / 2f;
                roadMesh.AddQuad(
                    innerEndPoint1,
                    innerMidPoint,
                    outerEndPoint1,
                    outerMidPoint

                );
                roadMesh.AddQuadUV0(
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f)
                );
                roadMesh.AddQuad(
                    innerMidPoint,
                    innerEndPoint2,
                    outerMidPoint,
                    outerEndPoint2
                );
                roadMesh.AddQuadUV0(
                    new Vector2(1f, 0f),
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 0f)
                );
            }
        }

        public string GetDebugCellLayout()
        {
            RectCell[,] cellGrid = new RectCell[chunkSizeX, chunkSizeZ];
            foreach (RectCell cell in cellList) {
                if (cell?.coordinates == null) {
                    continue;
                }

                int localX = cell.coordinates.x - startCellX;
                int localZ = cell.coordinates.z - startCellZ;
                if (localX < 0 || localX >= chunkSizeX || localZ < 0 || localZ >= chunkSizeZ) {
                    continue;
                }

                cellGrid[localX, localZ] = cell;
            }

            var sb = new System.Text.StringBuilder();
            for (int z = chunkSizeZ - 1; z >= 0; z--) {
                for (int x = 0; x < chunkSizeX; x++) {
                    if (x > 0) {
                        sb.Append(" | ");
                    }

                    RectCell cell = cellGrid[x, z];
                    if (cell?.coordinates == null) {
                        sb.Append("(null)");
                        continue;
                    }

                    sb.Append($"({x},{z})");
                }

                if (z > 0) {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        [ContextMenu("Debug Print All Cells")]
        public void DebugPrintAllCells()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"RectGridChunk cells count: {cellList.Count}");
            sb.AppendLine();
            sb.Append(GetDebugCellLayout());

            XNLogger.LogInfo(
                sb.ToString(),
                ("startCellX", startCellX.ToString()),
                ("startCellZ", startCellZ.ToString()),
                ("chunkSizeX", chunkSizeX.ToString()),
                ("chunkSizeZ", chunkSizeZ.ToString())
            );
        }
    }

}
