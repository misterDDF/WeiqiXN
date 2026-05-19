using UnityEngine;

namespace XNClient.ChessBoard
{
    public class RectCell
    {
        public RectGridChunk chunk;
        public RectCoordinates coordinates;
        public bool isOnEdge;
        // 共用边的四个邻接cell
        public RectCell[] neighbors = new RectCell[4];
        public int textureIndex = 0;
        public Vector3 centerPosInChunk
        {
            get
            {
                if (chunk == null || coordinates == null) {
                    return Vector3.zero;
                }

                float localX = (coordinates.x - chunk.startCellX + 0.5f) * ChessBoardConfig.rectCellSideLength;
                float localZ = (chunk.chunkSizeZ - (coordinates.z - chunk.startCellZ) - 0.5f) * ChessBoardConfig.rectCellSideLength;
                return new Vector3(localX, 0f, localZ);
            }
        }

        public RectCell(RectGridChunk chunk, RectCoordinates coordinates)
        {
            this.chunk = chunk;
            this.coordinates = coordinates;
        }

        public bool TryGetLineNeighbor(RectDirection dir, out RectCell neighbor)
        {
            neighbor = neighbors[(int)dir];
            return neighbor != null;
        }

        public bool TryGetPointNeighbor(RectDirection dir, out RectCell neighbor)
        {
            neighbor = null;
            RectCell lineNeighbor = neighbors[(int)dir];
            if (lineNeighbor != null) {
                neighbor = lineNeighbor.neighbors[(int)(dir.GetPrevDirection())];
                return neighbor != null;
            }
            return false;
        }
    }

}
