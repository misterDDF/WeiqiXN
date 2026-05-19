using UnityEngine;

namespace XNClient.ChessBoard
{
    public class RectCoordinates
    {
        public static Vector3 zeroPos = Vector3.zero;

        // Coordinates follow KataGo board layout: x grows left to right, z grows top to bottom.
        public int x;
        public int z;

        public RectCoordinates(int x, int z)
        {
            SetValue(x, z);
        }

        public void SetValue(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public RectCoordinates Clone()
        {
            return new RectCoordinates(x, z);
        }

        public override string ToString()
        {
            return $"(x:{x}, z:{z})";
        }
    }
}
