using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace XNClient.ChessBoard
{
    // 盘面标记的公共构件：平铺在盘面上、朝上的四边形（形状由 BoardOverlay.shader 按 UV 绘制）与 TMP 文字（默认字体思源黑体）。
    // 棋盘上的坐标、推荐点、形势与棋子上的手数、最后一手都经由这里创建，保证字形、尺寸和色值口径一致。
    public static class BoardSurfaceMarker
    {
        // 色值与 modules/14-visual-polish-plan.md 设计令牌一致：墨 Ink、纸 Paper。
        public static readonly Color Ink = new Color32(0x1F, 0x1C, 0x19, 0xFF);
        public static readonly Color Paper = new Color32(0xF3, 0xEF, 0xE6, 0xFF);

        // 最后一手圆点直径（按格宽计），约为棋子直径的三成。
        private const float LatestMoveMarkerSizeFactor = 0.26f;
        private const int LatestMoveMarkerSortingOrder = 20;
        private const float MoveNumberFontSize = 18f;
        private const int MoveNumberSortingOrder = 20;
        // 三位数按比例缩小，保证落在棋子直径内。
        private const float ThreeDigitFontScale = 0.8f;
        private static readonly Color MoveNumberOnBlackStoneColor = new Color(Paper.r, Paper.g, Paper.b, 0.92f);
        private static readonly Color MoveNumberOnWhiteStoneColor = new Color(Ink.r, Ink.g, Ink.b, 0.9f);

        private static Mesh quadMesh;

        public static MeshRenderer CreateQuad(Transform parent, string objectName, Vector3 localPosition, float size, Material material, int sortingOrder)
        {
            GameObject quad = new GameObject(objectName);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localScale = new Vector3(size, 1f, size);

            quad.AddComponent<MeshFilter>().sharedMesh = GetQuadMesh();
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureRenderer(renderer, sortingOrder);
            return renderer;
        }

        public static TextMeshPro CreateLabel(Transform parent, string objectName, string text, Vector3 localPosition, float fontSize, Color color, int sortingOrder)
        {
            GameObject labelGO = new GameObject(objectName);
            labelGO.transform.SetParent(parent, false);

            TextMeshPro label = labelGO.AddComponent<TextMeshPro>();
            RectTransform rectTransform = label.rectTransform;
            rectTransform.localPosition = localPosition;
            rectTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rectTransform.localScale = Vector3.one;
            rectTransform.sizeDelta = Vector2.zero;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            // 竖直方向按字形几何居中：数字与字母没有下伸部，视觉中心正好落在锚点上。
            label.alignment = TextAlignmentOptions.Midline;
            label.fontSize = fontSize;
            // TMP 的 SDF shader 不转换顶点色，线性色彩空间下先把 sRGB 色值转成线性值。
            label.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            label.text = text;
            ConfigureRenderer(label.renderer, sortingOrder);
            return label;
        }

        public static MeshRenderer CreateLatestMoveMarker(Transform parent, string objectName, Vector3 localPosition, Material material)
        {
            return CreateQuad(
                parent,
                objectName,
                localPosition,
                ChessBoardConfig.rectCellSideLength * LatestMoveMarkerSizeFactor,
                material,
                LatestMoveMarkerSortingOrder);
        }

        public static TextMeshPro CreateMoveNumber(Transform parent, string objectName, int moveNumber, bool isBlackStone, Vector3 localPosition)
        {
            return CreateLabel(
                parent,
                objectName,
                moveNumber.ToString(),
                localPosition,
                ResolveNumberFontSize(moveNumber, MoveNumberFontSize),
                isBlackStone ? MoveNumberOnBlackStoneColor : MoveNumberOnWhiteStoneColor,
                MoveNumberSortingOrder);
        }

        public static float ResolveNumberFontSize(int number, float fontSize)
        {
            return number >= 100 ? fontSize * ThreeDigitFontScale : fontSize;
        }

        // 把世界长度换算成 TMP 富文本的 em 值（非正交 TMP 中 1em = fontSize × 0.1 世界单位），用于 <mspace>、<line-height>。
        public static string ToEm(float worldLength, float fontSize)
        {
            return (worldLength / (fontSize * 0.1f)).ToString("0.####", CultureInfo.InvariantCulture) + "em";
        }

        private static void ConfigureRenderer(Renderer renderer, int sortingOrder)
        {
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = sortingOrder;
        }

        // 位于 XZ 平面、朝上的单位四边形，UV 覆盖 0..1。
        private static Mesh GetQuadMesh()
        {
            if (quadMesh != null) {
                return quadMesh;
            }

            quadMesh = new Mesh
            {
                name = "BoardSurfaceMarkerQuad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(1f, 0f),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            quadMesh.RecalculateNormals();
            return quadMesh;
        }
    }
}
