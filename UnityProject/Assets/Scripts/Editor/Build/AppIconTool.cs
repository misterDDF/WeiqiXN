using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class AppIconTool
{
    private const int IconSize = 1024;
    private const string IconFolder = "Assets/AppIcon";
    private const string IconPngPath = IconFolder + "/WeiqiXN_AppIcon.png";
    private const string IconIcoPath = IconFolder + "/WeiqiXN_AppIcon.ico";

    [MenuItem(CustomEditorMenuPaths.BuildPreprocess + "/生成并应用App图标")]
    public static void GenerateAndApplyAppIcon()
    {
        EnsureIconFolder();
        Texture2D icon = GenerateIconTexture(IconSize);
        File.WriteAllBytes(AssetPathToFullPath(IconPngPath), icon.EncodeToPNG());
        File.WriteAllBytes(AssetPathToFullPath(IconIcoPath), BuildIcoBytes(icon));
        UnityEngine.Object.DestroyImmediate(icon);

        AssetDatabase.ImportAsset(IconPngPath, ImportAssetOptions.ForceUpdate);
        ConfigureIconImporter(IconPngPath);
        AssetDatabase.ImportAsset(IconPngPath, ImportAssetOptions.ForceUpdate);

        Texture2D iconAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPngPath);
        if (iconAsset == null) {
            throw new FileNotFoundException("Generated app icon asset could not be loaded.", IconPngPath);
        }

        ApplyPlayerIcons(iconAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"App icon generated and applied. png: {IconPngPath}, ico: {IconIcoPath}");
    }

    public static void EnsureAppIconApplied()
    {
        EnsureIconFolder();
        string pngFullPath = AssetPathToFullPath(IconPngPath);
        string icoFullPath = AssetPathToFullPath(IconIcoPath);
        if (!File.Exists(pngFullPath) || !File.Exists(icoFullPath)) {
            GenerateAndApplyAppIcon();
            return;
        }

        AssetDatabase.ImportAsset(IconPngPath, ImportAssetOptions.ForceUpdate);
        ConfigureIconImporter(IconPngPath);
        Texture2D iconAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPngPath);
        if (iconAsset == null) {
            throw new FileNotFoundException("App icon asset could not be loaded.", IconPngPath);
        }

        ApplyPlayerIcons(iconAsset);
        AssetDatabase.SaveAssets();
    }

    private static Texture2D GenerateIconTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        Color32[] pixels = new Color32[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.48f;
        float cornerRadius = size * 0.225f;
        Rect boardRect = new Rect(size * 0.165f, size * 0.165f, size * 0.67f, size * 0.67f);
        float boardCorner = size * 0.085f;

        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float nx = (x - center.x) / radius;
                float ny = (y - center.y) / radius;
                float vignette = Mathf.Clamp01(1f - (nx * nx + ny * ny) * 0.42f);
                float sweep = Mathf.Clamp01((x + y) / (float)(size * 2));
                Color baseColor = Color.Lerp(new Color(0.035f, 0.095f, 0.13f), new Color(0.045f, 0.23f, 0.22f), sweep);
                Color glowColor = Color.Lerp(baseColor, new Color(0.93f, 0.63f, 0.18f), Mathf.Pow(vignette, 5f) * 0.32f);
                float mask = RoundedRectMask(x, y, new Rect(size * 0.04f, size * 0.04f, size * 0.92f, size * 0.92f), cornerRadius, 3f);
                pixels[y * size + x] = ToColor32(new Color(glowColor.r, glowColor.g, glowColor.b, mask));
            }
        }

        DrawDropShadow(pixels, size, boardRect, boardCorner, size * 0.022f, new Color(0f, 0f, 0f, 0.38f));
        DrawRoundedRect(pixels, size, boardRect, boardCorner, new Color(0.78f, 0.53f, 0.25f, 1f), new Color(0.97f, 0.75f, 0.36f, 1f));
        DrawBoardGrid(pixels, size, boardRect);

        Vector2 blackStone = new Vector2(size * 0.39f, size * 0.58f);
        Vector2 whiteStone = new Vector2(size * 0.62f, size * 0.42f);
        float stoneRadius = size * 0.145f;
        DrawStoneShadow(pixels, size, blackStone, stoneRadius);
        DrawStoneShadow(pixels, size, whiteStone, stoneRadius);
        DrawStone(pixels, size, blackStone, stoneRadius, false);
        DrawStone(pixels, size, whiteStone, stoneRadius, true);

        DrawAccentCross(pixels, size, boardRect);

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static void DrawRoundedRect(Color32[] pixels, int size, Rect rect, float radius, Color bottomColor, Color topColor)
    {
        int xMin = Mathf.Max(0, Mathf.FloorToInt(rect.xMin) - 2);
        int xMax = Mathf.Min(size - 1, Mathf.CeilToInt(rect.xMax) + 2);
        int yMin = Mathf.Max(0, Mathf.FloorToInt(rect.yMin) - 2);
        int yMax = Mathf.Min(size - 1, Mathf.CeilToInt(rect.yMax) + 2);
        for (int y = yMin; y <= yMax; y++) {
            float t = Mathf.InverseLerp(rect.yMin, rect.yMax, y);
            Color color = Color.Lerp(bottomColor, topColor, t);
            for (int x = xMin; x <= xMax; x++) {
                float mask = RoundedRectMask(x, y, rect, radius, 2f);
                if (mask <= 0f) {
                    continue;
                }

                BlendPixel(pixels, size, x, y, color, mask);
            }
        }
    }

    private static void DrawDropShadow(Color32[] pixels, int size, Rect rect, float radius, float offset, Color color)
    {
        Rect shadowRect = new Rect(rect.x + offset, rect.y + offset * 1.2f, rect.width, rect.height);
        int xMin = Mathf.Max(0, Mathf.FloorToInt(shadowRect.xMin - offset * 2f));
        int xMax = Mathf.Min(size - 1, Mathf.CeilToInt(shadowRect.xMax + offset * 2f));
        int yMin = Mathf.Max(0, Mathf.FloorToInt(shadowRect.yMin - offset * 2f));
        int yMax = Mathf.Min(size - 1, Mathf.CeilToInt(shadowRect.yMax + offset * 2f));
        for (int y = yMin; y <= yMax; y++) {
            for (int x = xMin; x <= xMax; x++) {
                float mask = RoundedRectMask(x, y, shadowRect, radius, offset * 2.5f);
                if (mask > 0f) {
                    BlendPixel(pixels, size, x, y, color, mask);
                }
            }
        }
    }

    private static void DrawBoardGrid(Color32[] pixels, int size, Rect rect)
    {
        Color lineColor = new Color(0.18f, 0.105f, 0.055f, 0.62f);
        int gridCount = 9;
        float pad = rect.width * 0.13f;
        float left = rect.xMin + pad;
        float right = rect.xMax - pad;
        float bottom = rect.yMin + pad;
        float top = rect.yMax - pad;
        for (int i = 0; i < gridCount; i++) {
            float t = i / (float)(gridCount - 1);
            float x = Mathf.Lerp(left, right, t);
            float y = Mathf.Lerp(bottom, top, t);
            DrawLine(pixels, size, new Vector2(x, bottom), new Vector2(x, top), size * 0.0046f, lineColor);
            DrawLine(pixels, size, new Vector2(left, y), new Vector2(right, y), size * 0.0046f, lineColor);
        }

        float dotRadius = size * 0.014f;
        DrawCircle(pixels, size, new Vector2(Mathf.Lerp(left, right, 0.5f), Mathf.Lerp(bottom, top, 0.5f)), dotRadius, lineColor);
        DrawCircle(pixels, size, new Vector2(Mathf.Lerp(left, right, 0.25f), Mathf.Lerp(bottom, top, 0.25f)), dotRadius, lineColor);
        DrawCircle(pixels, size, new Vector2(Mathf.Lerp(left, right, 0.75f), Mathf.Lerp(bottom, top, 0.75f)), dotRadius, lineColor);
    }

    private static void DrawStoneShadow(Color32[] pixels, int size, Vector2 center, float radius)
    {
        DrawCircle(pixels, size, center + new Vector2(radius * 0.12f, radius * 0.16f), radius * 1.04f, new Color(0f, 0f, 0f, 0.34f));
    }

    private static void DrawStone(Color32[] pixels, int size, Vector2 center, float radius, bool white)
    {
        int xMin = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 2));
        int xMax = Mathf.Min(size - 1, Mathf.CeilToInt(center.x + radius + 2));
        int yMin = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 2));
        int yMax = Mathf.Min(size - 1, Mathf.CeilToInt(center.y + radius + 2));
        for (int y = yMin; y <= yMax; y++) {
            for (int x = xMin; x <= xMax; x++) {
                Vector2 delta = new Vector2(x - center.x, y - center.y);
                float dist = delta.magnitude;
                float mask = Mathf.Clamp01((radius - dist) / 2f + 0.5f);
                if (mask <= 0f) {
                    continue;
                }

                float light = Mathf.Clamp01(1f - dist / radius);
                float highlight = Mathf.Clamp01(1f - (delta - new Vector2(-radius * 0.33f, radius * 0.38f)).magnitude / (radius * 0.9f));
                Color baseColor = white
                    ? Color.Lerp(new Color(0.70f, 0.66f, 0.58f), new Color(1f, 0.97f, 0.86f), light * 0.7f)
                    : Color.Lerp(new Color(0.015f, 0.017f, 0.018f), new Color(0.13f, 0.14f, 0.14f), light * 0.65f);
                Color color = Color.Lerp(baseColor, Color.white, highlight * (white ? 0.28f : 0.12f));
                BlendPixel(pixels, size, x, y, color, mask);
            }
        }
    }

    private static void DrawAccentCross(Color32[] pixels, int size, Rect rect)
    {
        Color accent = new Color(1f, 0.78f, 0.36f, 0.58f);
        DrawLine(pixels, size, new Vector2(rect.xMin + rect.width * 0.2f, rect.yMin + rect.height * 0.2f), new Vector2(rect.xMax - rect.width * 0.2f, rect.yMax - rect.height * 0.2f), size * 0.018f, accent);
        DrawLine(pixels, size, new Vector2(rect.xMin + rect.width * 0.25f, rect.yMax - rect.height * 0.18f), new Vector2(rect.xMax - rect.width * 0.18f, rect.yMin + rect.height * 0.25f), size * 0.010f, new Color(1f, 0.92f, 0.55f, 0.32f));
    }

    private static void DrawLine(Color32[] pixels, int size, Vector2 start, Vector2 end, float width, Color color)
    {
        float minX = Mathf.Min(start.x, end.x) - width - 2f;
        float maxX = Mathf.Max(start.x, end.x) + width + 2f;
        float minY = Mathf.Min(start.y, end.y) - width - 2f;
        float maxY = Mathf.Max(start.y, end.y) + width + 2f;
        Vector2 segment = end - start;
        float lengthSqr = segment.sqrMagnitude;
        int xMin = Mathf.Max(0, Mathf.FloorToInt(minX));
        int xMax = Mathf.Min(size - 1, Mathf.CeilToInt(maxX));
        int yMin = Mathf.Max(0, Mathf.FloorToInt(minY));
        int yMax = Mathf.Min(size - 1, Mathf.CeilToInt(maxY));
        for (int y = yMin; y <= yMax; y++) {
            for (int x = xMin; x <= xMax; x++) {
                Vector2 point = new Vector2(x, y);
                float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr);
                float dist = (point - (start + segment * t)).magnitude;
                float mask = Mathf.Clamp01((width - dist) / 1.5f + 0.5f);
                if (mask > 0f) {
                    BlendPixel(pixels, size, x, y, color, mask);
                }
            }
        }
    }

    private static void DrawCircle(Color32[] pixels, int size, Vector2 center, float radius, Color color)
    {
        int xMin = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 2));
        int xMax = Mathf.Min(size - 1, Mathf.CeilToInt(center.x + radius + 2));
        int yMin = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 2));
        int yMax = Mathf.Min(size - 1, Mathf.CeilToInt(center.y + radius + 2));
        for (int y = yMin; y <= yMax; y++) {
            for (int x = xMin; x <= xMax; x++) {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float mask = Mathf.Clamp01((radius - dist) / 1.5f + 0.5f);
                if (mask > 0f) {
                    BlendPixel(pixels, size, x, y, color, mask);
                }
            }
        }
    }

    private static float RoundedRectMask(float x, float y, Rect rect, float radius, float softness)
    {
        Vector2 center = new Vector2(rect.center.x, rect.center.y);
        Vector2 half = new Vector2(rect.width * 0.5f - radius, rect.height * 0.5f - radius);
        Vector2 point = new Vector2(Mathf.Abs(x - center.x), Mathf.Abs(y - center.y));
        Vector2 excess = new Vector2(Mathf.Max(point.x - half.x, 0f), Mathf.Max(point.y - half.y, 0f));
        float dist = excess.magnitude - radius;
        return Mathf.Clamp01((-dist + softness) / Mathf.Max(softness, 0.001f));
    }

    private static void BlendPixel(Color32[] pixels, int size, int x, int y, Color src, float coverage)
    {
        int index = y * size + x;
        Color dst = pixels[index];
        float srcAlpha = Mathf.Clamp01(src.a * coverage);
        float outAlpha = srcAlpha + dst.a * (1f - srcAlpha);
        if (outAlpha <= 0f) {
            pixels[index] = new Color32(0, 0, 0, 0);
            return;
        }

        Color outColor = (src * srcAlpha + dst * dst.a * (1f - srcAlpha)) / outAlpha;
        outColor.a = outAlpha;
        pixels[index] = ToColor32(outColor);
    }

    private static Color32 ToColor32(Color color)
    {
        return new Color32(
            (byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(color.a) * 255f));
    }

    private static byte[] BuildIcoBytes(Texture2D source)
    {
        int[] sizes = { 256, 128, 64, 48, 32, 16 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int size in sizes) {
            Texture2D scaled = ScaleTexture(source, size);
            pngs.Add(scaled.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(scaled);
        }

        using (MemoryStream stream = new MemoryStream()) {
            WriteUInt16(stream, 0);
            WriteUInt16(stream, 1);
            WriteUInt16(stream, (ushort)pngs.Count);
            int offset = 6 + pngs.Count * 16;
            for (int i = 0; i < pngs.Count; i++) {
                int size = sizes[i];
                byte[] png = pngs[i];
                stream.WriteByte((byte)(size >= 256 ? 0 : size));
                stream.WriteByte((byte)(size >= 256 ? 0 : size));
                stream.WriteByte(0);
                stream.WriteByte(0);
                WriteUInt16(stream, 1);
                WriteUInt16(stream, 32);
                WriteUInt32(stream, (uint)png.Length);
                WriteUInt32(stream, (uint)offset);
                offset += png.Length;
            }

            foreach (byte[] png in pngs) {
                stream.Write(png, 0, png.Length);
            }
            return stream.ToArray();
        }
    }

    private static Texture2D ScaleTexture(Texture2D source, int size)
    {
        Texture2D scaled = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;
                pixels[y * size + x] = source.GetPixelBilinear(u, v);
            }
        }
        scaled.SetPixels(pixels);
        scaled.Apply(false, false);
        return scaled;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 24) & 0xff));
    }

    private static void ConfigureIconImporter(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) {
            throw new InvalidOperationException($"Texture importer not found: {assetPath}");
        }

        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.maxTextureSize = IconSize;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void ApplyPlayerIcons(Texture2D icon)
    {
        ApplyTargetGroupIcons(BuildTargetGroup.Unknown, icon);
        ApplyTargetGroupIcons(BuildTargetGroup.Standalone, icon);
        ApplyTargetGroupIcons(BuildTargetGroup.Android, icon);
        ApplyTargetGroupIcons(BuildTargetGroup.iOS, icon);
        ApplyPlatformIcons(icon, BuildTargetGroup.Android);
        ApplyPlatformIcons(icon, BuildTargetGroup.iOS);
    }

    private static void ApplyTargetGroupIcons(BuildTargetGroup targetGroup, Texture2D icon)
    {
        Texture2D[] currentIcons = PlayerSettings.GetIconsForTargetGroup(targetGroup);
        int iconCount = currentIcons != null && currentIcons.Length > 0 ? currentIcons.Length : 1;
        Texture2D[] icons = new Texture2D[iconCount];
        for (int i = 0; i < icons.Length; i++) {
            icons[i] = icon;
        }

        PlayerSettings.SetIconsForTargetGroup(targetGroup, icons);
    }

    private static void ApplyPlatformIcons(Texture2D icon, BuildTargetGroup targetGroup)
    {
        MethodInfo[] methods = typeof(PlayerSettings).GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (MethodInfo getMethod in methods) {
            if (!IsPlatformIconGetter(getMethod)) {
                continue;
            }

            Type kindType = getMethod.GetParameters()[1].ParameterType;
            foreach (object kind in EnumeratePlatformIconKinds(kindType)) {
                TryApplyPlatformIconKind(icon, targetGroup, getMethod, kind);
            }
        }
    }

    private static bool IsPlatformIconGetter(MethodInfo method)
    {
        if (method.Name != "GetPlatformIcons") {
            return false;
        }

        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 2
            && parameters[0].ParameterType == typeof(BuildTargetGroup)
            && method.ReturnType.IsArray;
    }

    private static IEnumerable<object> EnumeratePlatformIconKinds(Type kindType)
    {
        HashSet<object> kinds = new HashSet<object>();
        if (kindType.IsEnum) {
            foreach (object value in Enum.GetValues(kindType)) {
                if (kinds.Add(value)) {
                    yield return value;
                }
            }
            yield break;
        }

        foreach (object kind in EnumerateStaticPlatformIconKinds(kindType, kindType)) {
            if (kinds.Add(kind)) {
                yield return kind;
            }
        }

        Type[] editorTypes;
        try {
            editorTypes = typeof(PlayerSettings).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex) {
            editorTypes = ex.Types;
        }

        foreach (Type editorType in editorTypes) {
            if (editorType == null || !editorType.Name.EndsWith("PlatformIconKind", StringComparison.Ordinal)) {
                continue;
            }

            foreach (object kind in EnumerateStaticPlatformIconKinds(editorType, kindType)) {
                if (kinds.Add(kind)) {
                    yield return kind;
                }
            }
        }
    }

    private static IEnumerable<object> EnumerateStaticPlatformIconKinds(Type ownerType, Type kindType)
    {
        foreach (FieldInfo field in ownerType.GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (!kindType.IsAssignableFrom(field.FieldType)) {
                continue;
            }

            object value = field.GetValue(null);
            if (value != null) {
                yield return value;
            }
        }

        foreach (PropertyInfo property in ownerType.GetProperties(BindingFlags.Public | BindingFlags.Static)) {
            if (!kindType.IsAssignableFrom(property.PropertyType) || property.GetIndexParameters().Length > 0) {
                continue;
            }

            object value = property.GetValue(null, null);
            if (value != null) {
                yield return value;
            }
        }
    }

    private static void TryApplyPlatformIconKind(Texture2D icon, BuildTargetGroup targetGroup, MethodInfo getMethod, object kind)
    {
        Array platformIcons;
        try {
            platformIcons = getMethod.Invoke(null, new[] { (object)targetGroup, kind }) as Array;
        }
        catch (TargetInvocationException) {
            return;
        }
        catch (ArgumentException) {
            return;
        }

        if (platformIcons == null || platformIcons.Length == 0) {
            return;
        }

        bool changed = false;
        for (int i = 0; i < platformIcons.Length; i++) {
            object platformIcon = platformIcons.GetValue(i);
            if (platformIcon == null || !TrySetPlatformIconTexture(platformIcon, icon)) {
                continue;
            }

            platformIcons.SetValue(platformIcon, i);
            changed = true;
        }

        if (!changed) {
            return;
        }

        MethodInfo setMethod = FindPlatformIconSetter(getMethod.GetParameters()[1].ParameterType, platformIcons.GetType());
        if (setMethod == null) {
            return;
        }

        try {
            setMethod.Invoke(null, new[] { (object)targetGroup, kind, platformIcons });
        }
        catch (TargetInvocationException) {
        }
        catch (ArgumentException) {
        }
    }

    private static MethodInfo FindPlatformIconSetter(Type kindType, Type iconsArrayType)
    {
        foreach (MethodInfo method in typeof(PlayerSettings).GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            if (method.Name != "SetPlatformIcons") {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 3
                && parameters[0].ParameterType == typeof(BuildTargetGroup)
                && parameters[1].ParameterType == kindType
                && parameters[2].ParameterType.IsAssignableFrom(iconsArrayType)) {
                return method;
            }
        }

        return null;
    }

    private static bool TrySetPlatformIconTexture(object platformIcon, Texture2D icon)
    {
        Type platformIconType = platformIcon.GetType();
        foreach (MethodInfo method in platformIconType.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
            if (method.Name != "SetTextures") {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsArray) {
                method.Invoke(platformIcon, new object[] { new[] { icon } });
                return true;
            }
        }

        foreach (MethodInfo method in platformIconType.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
            if (method.Name != "SetTexture") {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(typeof(Texture2D))) {
                method.Invoke(platformIcon, new object[] { icon });
                return true;
            }

            if (parameters.Length == 2
                && parameters[0].ParameterType.IsAssignableFrom(typeof(Texture2D))
                && parameters[1].ParameterType == typeof(int)) {
                method.Invoke(platformIcon, new object[] { icon, 0 });
                return true;
            }
        }

        return false;
    }

    private static void EnsureIconFolder()
    {
        if (AssetDatabase.IsValidFolder(IconFolder)) {
            return;
        }

        AssetDatabase.CreateFolder("Assets", "AppIcon");
    }

    private static string AssetPathToFullPath(string assetPath)
    {
        string relativePath = assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Application.dataPath, relativePath);
    }
}
