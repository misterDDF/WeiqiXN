#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// 程序生成 V3 UI 主题贴图：白色 + alpha 的九宫格与线性图标，由 Image.color 用 UIPalette 令牌着色，一张图覆盖多种状态。
// 按 4 倍精度绘制并开 mipmap，1x 屏取 mip 2 仍有正确的覆盖率抗锯齿，高分屏不发糊。
// 内容不变时不重写 PNG、不重设导入参数，可重复执行。
public static class UIThemeTextureTool
{
    public const string Folder = "Assets/UI/Textures/Theme";

    public const string Panel = "ui_panel";
    public const string CardShadow = "ui_card_shadow";
    public const string Button = "ui_button";
    public const string Input = "ui_input";
    public const string ToggleBox = "ui_toggle_box";
    public const string Capsule = "ui_capsule";
    public const string Knob = "ui_knob";

    public static readonly string[] IconNames = {
        "icon_close", "icon_left", "icon_right", "icon_down", "icon_first", "icon_last", "icon_check",
        "icon_info", "icon_analysis", "icon_user", "icon_try", "icon_refresh", "icon_export",
        "icon_up", "icon_pass", "icon_ownership", "icon_search", "icon_menu",
    };

    // 卡片柔影比面板四边各外扩这么多 UI 单位；使用时柔影 Image 比面板大 2 倍此值并略向下偏移。
    public const float CardShadowExtent = 16f;

    private const int DetailScale = 4;
    private const int ShadowDetailScale = 2;
    private const int IconSize = 128;
    private const float IconGrid = 24f;
    private const float IconStroke = 1.75f;

    private readonly struct ThemeTexture
    {
        public readonly string Name;
        public readonly int Size;
        public readonly int DetailScale;
        public readonly int Border;
        public readonly Func<float, float, Vector2> Shade;

        public ThemeTexture(string name, int size, int detailScale, int border, Func<float, float, Vector2> shade)
        {
            Name = name;
            Size = size;
            DetailScale = detailScale;
            Border = border;
            Shade = shade;
        }
    }

    public static string SpritePath(string name)
    {
        return $"{Folder}/{name}.png";
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/生成主题贴图")]
    public static void Generate()
    {
        Directory.CreateDirectory(Folder);
        ThemeTexture[] textures = BuildDefinitions();
        int written = 0;
        foreach (ThemeTexture texture in textures) {
            if (WritePngIfChanged(texture)) {
                written++;
            }
        }

        AssetDatabase.Refresh();
        int reimported = 0;
        foreach (ThemeTexture texture in textures) {
            if (ApplyImportSettings(texture)) {
                reimported++;
            }
        }

        Debug.Log($"Theme textures checked. count: {textures.Length}, written: {written}, reimported: {reimported}");
    }

    private static ThemeTexture[] BuildDefinitions()
    {
        // 边缘明度系数：着 Paper 色时 1px 边缘等于 Hairline 叠在 Paper 上，着其他颜色时是同色相的略深边缘。
        Color paper = UIPalette.Paper.linear;
        Color hairline = UIPalette.Hairline.linear;
        float rimShade = Mathf.Lerp(1f, hairline.g / paper.g, UIPalette.Hairline.a);

        ThemeTexture[] textures = new ThemeTexture[7 + IconNames.Length];
        textures[0] = new ThemeTexture(Panel, 64, DetailScale, 28, (x, y) => RoundedRectWithRim(x, y, 64, 6f * DetailScale, rimShade));
        textures[1] = new ThemeTexture(CardShadow, 128, ShadowDetailScale, 48, (x, y) => SoftShadow(x, y, 128, CardShadowExtent * ShadowDetailScale, 6f * ShadowDetailScale, 5f * ShadowDetailScale));
        textures[2] = new ThemeTexture(Button, 64, DetailScale, 20, (x, y) => RoundedRectWithRim(x, y, 64, 4f * DetailScale, rimShade));
        textures[3] = new ThemeTexture(Input, 64, DetailScale, 24, (x, y) => SunkenRect(x, y, 64, 4f * DetailScale, rimShade));
        textures[4] = new ThemeTexture(ToggleBox, 64, DetailScale, 16, (x, y) => RoundedRectWithRim(x, y, 64, 3f * DetailScale, rimShade));
        textures[5] = new ThemeTexture(Capsule, 64, DetailScale, 31, (x, y) => new Vector2(1f, Coverage(Circle(x, y, 32f, 32f, 31f))));
        textures[6] = new ThemeTexture(Knob, 128, DetailScale, 0, (x, y) => KnobShade(x, y, rimShade));
        for (int i = 0; i < IconNames.Length; i++) {
            Func<float, float, float> distance = IconDistance(IconNames[i]);
            textures[7 + i] = new ThemeTexture(IconNames[i], IconSize, DetailScale, 0, (x, y) => IconShade(x, y, distance));
        }

        return textures;
    }

    private static bool WritePngIfChanged(ThemeTexture definition)
    {
        int size = definition.Size;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                // Texture2D 第 0 行在底部，形状函数用左上角为原点、向下为正的坐标。
                Vector2 shade = definition.Shade(x + 0.5f, size - y - 0.5f);
                byte rgb = ToByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(shade.x)));
                pixels[y * size + x] = new Color32(rgb, rgb, rgb, ToByte(shade.y));
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        byte[] png;
        try {
            texture.SetPixels32(pixels);
            png = texture.EncodeToPNG();
        }
        finally {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        string path = SpritePath(definition.Name);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(png)) {
            return false;
        }

        File.WriteAllBytes(path, png);
        return true;
    }

    private static bool ApplyImportSettings(ThemeTexture definition)
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath(definition.Name));
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        Vector4 border = new Vector4(definition.Border, definition.Border, definition.Border, definition.Border);
        float pixelsPerUnit = 100f * definition.DetailScale;
        bool changed = importer.textureType != TextureImporterType.Sprite
            || settings.spriteMode != (int)SpriteImportMode.Single
            || settings.spriteMeshType != SpriteMeshType.FullRect
            || !Mathf.Approximately(settings.spritePixelsPerUnit, pixelsPerUnit)
            || settings.spriteBorder != border
            || !settings.mipmapEnabled
            || settings.filterMode != FilterMode.Trilinear
            || settings.wrapMode != TextureWrapMode.Clamp
            || !settings.sRGBTexture
            || !settings.alphaIsTransparency
            || settings.npotScale != TextureImporterNPOTScale.None
            || importer.textureCompression != TextureImporterCompression.Uncompressed
            || importer.assetBundleName != BuildConfig.AB_LABEL_UI_TEXTURE;
        if (!changed) {
            return false;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.ReadTextureSettings(settings);
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spritePixelsPerUnit = pixelsPerUnit;
        settings.spriteBorder = border;
        settings.mipmapEnabled = true;
        settings.filterMode = FilterMode.Trilinear;
        settings.wrapMode = TextureWrapMode.Clamp;
        settings.sRGBTexture = true;
        settings.alphaIsTransparency = true;
        settings.npotScale = TextureImporterNPOTScale.None;
        importer.SetTextureSettings(settings);
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.assetBundleName = BuildConfig.AB_LABEL_UI_TEXTURE;
        importer.SaveAndReimport();
        return true;
    }

    // 形状整体内缩 1 texel，让贴图边界内留出抗锯齿过渡。
    private static Vector2 RoundedRectWithRim(float x, float y, int size, float radius, float rimShade)
    {
        float distance = RoundedRect(x, y, size * 0.5f, size * 0.5f, size * 0.5f - 1f, radius);
        return new Vector2(Rim(distance, rimShade), Coverage(distance));
    }

    // 输入框：带细线边缘，顶部 2 个单位内有一道由深到浅的内影，读作凹面。
    private static Vector2 SunkenRect(float x, float y, int size, float radius, float rimShade)
    {
        float distance = RoundedRect(x, y, size * 0.5f, size * 0.5f, size * 0.5f - 1f, radius);
        float depthFromTop = y - 1f - DetailScale;
        float innerShadow = Mathf.Lerp(Mathf.Lerp(rimShade, 1f, 0.5f), 1f, Mathf.SmoothStep(0f, 1f, depthFromTop / (2f * DetailScale)));
        return new Vector2(Mathf.Min(Rim(distance, rimShade), innerShadow), Coverage(distance));
    }

    // 圆角矩形的高斯柔影：用有符号距离近似卷积，sigma 以 texel 计。
    private static Vector2 SoftShadow(float x, float y, int size, float inset, float radius, float sigma)
    {
        float distance = RoundedRect(x, y, size * 0.5f, size * 0.5f, size * 0.5f - inset, radius);
        return new Vector2(1f, 0.5f * (1f - Erf(distance / (sigma * 1.41421356f))));
    }

    // 滑条手柄：带细线边缘的圆片，下方一层黑色柔影；着色时柔影保持黑色。
    private static Vector2 KnobShade(float x, float y, float rimShade)
    {
        float distance = Circle(x, y, 64f, 62f, 11f * DetailScale);
        float bodyAlpha = Coverage(distance);
        float shadowDistance = Circle(x, y, 64f, 62f + DetailScale, 11f * DetailScale);
        float shadowAlpha = 0.3f * 0.5f * (1f - Erf(shadowDistance / (1.5f * DetailScale * 1.41421356f)));
        float alpha = bodyAlpha + shadowAlpha * (1f - bodyAlpha);
        float shade = alpha > 0f ? Rim(distance, rimShade) * bodyAlpha / alpha : 1f;
        return new Vector2(shade, alpha);
    }

    private static Vector2 IconShade(float x, float y, Func<float, float, float> distance)
    {
        float scale = IconSize / IconGrid;
        float gridDistance = distance(x / scale, y / scale);
        return new Vector2(1f, Coverage(gridDistance * scale - IconStroke * 0.5f * scale));
    }

    // 线性图标按 24 格网格描述（左上角为原点），笔画 1.75、圆头；返回到笔画中心线的距离。
    private static Func<float, float, float> IconDistance(string name)
    {
        switch (name) {
            case "icon_close":
                return (x, y) => Mathf.Min(Segment(x, y, 6f, 6f, 18f, 18f), Segment(x, y, 18f, 6f, 6f, 18f));
            case "icon_left":
                return (x, y) => Polyline(x, y, 15f, 6f, 9f, 12f, 15f, 18f);
            case "icon_right":
                return (x, y) => Polyline(x, y, 9f, 6f, 15f, 12f, 9f, 18f);
            case "icon_down":
                return (x, y) => Polyline(x, y, 6f, 9f, 12f, 15f, 18f, 9f);
            case "icon_first":
                return (x, y) => Mathf.Min(Polyline(x, y, 17f, 6f, 11f, 12f, 17f, 18f), Segment(x, y, 7f, 6f, 7f, 18f));
            case "icon_last":
                return (x, y) => Mathf.Min(Polyline(x, y, 7f, 6f, 13f, 12f, 7f, 18f), Segment(x, y, 17f, 6f, 17f, 18f));
            case "icon_check":
                return (x, y) => Polyline(x, y, 5f, 12.5f, 10f, 17.5f, 19f, 7f);
            case "icon_info":
                return (x, y) => Mathf.Min(Mathf.Abs(Circle(x, y, 12f, 12f, 9f)), Mathf.Min(Segment(x, y, 12f, 11f, 12f, 16.5f), Segment(x, y, 12f, 7.75f, 12f, 7.75f)));
            case "icon_analysis":
                return (x, y) => Mathf.Min(Polyline(x, y, 3f, 3f, 3f, 21f, 21f, 21f), Polyline(x, y, 7f, 15f, 11f, 10f, 15f, 13f, 20f, 7f));
            case "icon_user":
                return (x, y) => Mathf.Min(Mathf.Abs(Circle(x, y, 12f, 7f, 4f)), Mathf.Min(
                    Mathf.Min(Segment(x, y, 5f, 21f, 5f, 19f), Arc(x, y, 9f, 19f, 4f, 180f, 270f)),
                    Mathf.Min(Mathf.Min(Segment(x, y, 9f, 15f, 15f, 15f), Arc(x, y, 15f, 19f, 4f, 270f, 360f)), Segment(x, y, 19f, 19f, 19f, 21f))));
            case "icon_try":
                // 分支：试下即从当前局面分出一条变化。
                return (x, y) => Mathf.Min(Mathf.Min(Segment(x, y, 6f, 3f, 6f, 15f), Mathf.Abs(Circle(x, y, 18f, 6f, 3f))),
                    Mathf.Min(Mathf.Abs(Circle(x, y, 6f, 18f, 3f)), Arc(x, y, 9f, 9f, 9f, 0f, 90f)));
            case "icon_refresh":
                return (x, y) => Mathf.Min(Arc(x, y, 12f, 12f, 9f, 0f, 315f), Mathf.Min(Segment(x, y, 18.364f, 5.636f, 21f, 8f), Polyline(x, y, 21f, 3f, 21f, 8f, 16f, 8f)));
            case "icon_export":
                return (x, y) => Mathf.Min(Mathf.Min(
                    Mathf.Min(Segment(x, y, 3f, 15f, 3f, 19f), Arc(x, y, 5f, 19f, 2f, 90f, 180f)),
                    Mathf.Min(Segment(x, y, 5f, 21f, 19f, 21f), Mathf.Min(Arc(x, y, 19f, 19f, 2f, 0f, 90f), Segment(x, y, 21f, 19f, 21f, 15f)))),
                    Mathf.Min(Polyline(x, y, 7f, 8f, 12f, 3f, 17f, 8f), Segment(x, y, 12f, 3f, 12f, 15f)));
            case "icon_up":
                return (x, y) => Polyline(x, y, 6f, 15f, 12f, 9f, 18f, 15f);
            case "icon_pass":
                // 圆内一道斜杠：这一手不下。
                return (x, y) => Mathf.Min(Mathf.Abs(Circle(x, y, 12f, 12f, 7.5f)), Segment(x, y, 8f, 16f, 16f, 8f));
            case "icon_ownership":
                // 田字四格，对角实心：归属分黑白两方。四格中心线同为边长 6 − 笔画的方框，实心格取内部负距离，外沿与描边格对齐。
                return (x, y) => Mathf.Min(
                    Mathf.Min(RoundedRect(x, y, 8f, 8f, 3f - IconStroke * 0.5f, 0f), RoundedRect(x, y, 16f, 16f, 3f - IconStroke * 0.5f, 0f)),
                    Mathf.Min(Mathf.Abs(RoundedRect(x, y, 16f, 8f, 3f - IconStroke * 0.5f, 0f)), Mathf.Abs(RoundedRect(x, y, 8f, 16f, 3f - IconStroke * 0.5f, 0f))));
            case "icon_search":
                return (x, y) => Mathf.Min(Mathf.Abs(Circle(x, y, 10f, 10f, 6f)), Segment(x, y, 14.5f, 14.5f, 20f, 20f));
            case "icon_menu":
                return (x, y) => Mathf.Min(Segment(x, y, 5f, 7f, 19f, 7f), Mathf.Min(Segment(x, y, 5f, 12f, 19f, 12f), Segment(x, y, 5f, 17f, 19f, 17f)));
            default:
                throw new ArgumentException($"Unknown theme icon: {name}");
        }
    }

    // 1px 细线边缘：从外沿向内 1 个 UI 单位内取边缘明度，之外为 1。
    private static float Rim(float distance, float rimShade)
    {
        return Mathf.Lerp(rimShade, 1f, Mathf.Clamp01(-distance - DetailScale + 0.5f));
    }

    private static float Coverage(float distance)
    {
        return Mathf.Clamp01(0.5f - distance);
    }

    private static float RoundedRect(float x, float y, float centerX, float centerY, float halfSize, float radius)
    {
        float qx = Mathf.Abs(x - centerX) - (halfSize - radius);
        float qy = Mathf.Abs(y - centerY) - (halfSize - radius);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private static float Circle(float x, float y, float centerX, float centerY, float radius)
    {
        return new Vector2(x - centerX, y - centerY).magnitude - radius;
    }

    private static float Segment(float x, float y, float ax, float ay, float bx, float by)
    {
        Vector2 point = new Vector2(x - ax, y - ay);
        Vector2 direction = new Vector2(bx - ax, by - ay);
        float lengthSquared = direction.sqrMagnitude;
        float t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point, direction) / lengthSquared) : 0f;
        return (point - direction * t).magnitude;
    }

    private static float Polyline(float x, float y, params float[] points)
    {
        float distance = float.MaxValue;
        for (int i = 0; i + 3 < points.Length; i += 2) {
            distance = Mathf.Min(distance, Segment(x, y, points[i], points[i + 1], points[i + 2], points[i + 3]));
        }

        return distance;
    }

    // 圆弧：角度按向下为正的坐标系计，0° 指向右、90° 指向下，从 startDegrees 顺时针扫到 endDegrees。
    private static float Arc(float x, float y, float centerX, float centerY, float radius, float startDegrees, float endDegrees)
    {
        float angle = Mathf.Atan2(y - centerY, x - centerX) * Mathf.Rad2Deg;
        float sweep = Mathf.Repeat(angle - startDegrees, 360f);
        if (sweep <= endDegrees - startDegrees) {
            return Mathf.Abs(Circle(x, y, centerX, centerY, radius));
        }

        float start = startDegrees * Mathf.Deg2Rad;
        float end = endDegrees * Mathf.Deg2Rad;
        return Mathf.Min(
            new Vector2(x - centerX - radius * Mathf.Cos(start), y - centerY - radius * Mathf.Sin(start)).magnitude,
            new Vector2(x - centerX - radius * Mathf.Cos(end), y - centerY - radius * Mathf.Sin(end)).magnitude);
    }

    // Abramowitz-Stegun 7.1.26，误差 < 1.5e-7，足够生成 8 位柔影。
    private static float Erf(float value)
    {
        float sign = Mathf.Sign(value);
        float x = Mathf.Abs(value);
        float t = 1f / (1f + 0.3275911f * x);
        float polynomial = ((((1.061405429f * t - 1.453152027f) * t + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t;
        return sign * (1f - polynomial * Mathf.Exp(-x * x));
    }

    private static byte ToByte(float value)
    {
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
    }
}
#endif
