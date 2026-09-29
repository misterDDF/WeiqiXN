#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Scene = UnityEngine.SceneManagement.Scene;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

// 编辑态离屏渲染 UI 主题样张和页面截图，供 V3 UI 美化对比；在预览场景里临时搭 World Space Canvas，不改任何页面或场景。
// theme_landscape：色板令牌、字号阶梯、字重；theme_controls：主题贴图拼出的控件与图标；page_*：各 Page prefab 的横竖屏默认状态。
// Regular 样张文字只用字体图集里已有的字符，缺字的文字跳过并警告，避免动态字体在编辑态追加字形而改动 10MB 的字体资产；
// Medium / Serif 是新资产，允许随样张缓存字形。
public static class UIThemePreviewCaptureTool
{
    private const string OutputFolder = "Temp/WeiqiXN/ThemePreview";
    private const string PageFolder = "Assets/UI/Prefab/Page";
    private const int Width = 1600;
    private const int Height = 900;
    private const int MsaaSamples = 4;
    private const float Margin = 40f;
    private const float CardPadding = 32f;
    private const float SwatchWidth = 124f;
    private const float SwatchHeight = 48f;
    private const float SwatchCellWidth = 140f;
    private const float SwatchCellHeight = 100f;
    private const int SwatchColumns = 5;
    private const string SampleText = "开始对局 复盘 胜率 12:34";

    private static readonly int[] FontSizeLadder = { 40, 28, 22, 18, 15 };

    private static TMP_FontAsset glyphFrozenFont;

    private readonly struct FontSample
    {
        public readonly string Label;
        public readonly TMP_FontAsset Font;
        public readonly FontStyles Style;

        public FontSample(string label, TMP_FontAsset font, FontStyles style)
        {
            Label = label;
            Font = font;
            Style = style;
        }
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/生成主题样张截图")]
    public static void Capture()
    {
        TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.RegularFontAssetPath);
        TMP_FontAsset medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.MediumFontAssetPath);
        TMP_FontAsset serif = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.SerifFontAssetPath);
        if (medium == null || serif == null) {
            Debug.LogWarning($"Theme fonts not found, run {CustomEditorMenuPaths.UI}/生成主题字体资产 first. Missing samples fall back to Regular.");
        }

        glyphFrozenFont = regular;
        try {
            FontSample[] fontSamples = {
                new FontSample("Regular", regular, FontStyles.Normal),
                new FontSample("Regular Bold", regular, FontStyles.Bold),
                new FontSample("Medium", medium, FontStyles.Normal),
                new FontSample("Serif SemiBold", serif, FontStyles.Normal),
            };
            RenderSheet("theme_landscape.png", canvas => BuildTokenSheet(canvas, regular, fontSamples));
            RenderSheet("theme_controls.png", canvas => BuildControlSheet(canvas, regular, medium != null ? medium : regular, serif != null ? serif : regular));
        }
        finally {
            glyphFrozenFont = null;
        }
    }

    // 页面实例临时改为 World Space Canvas，按参考分辨率 1:1 铺满画面，等同运行时缩放因子为 1 的 Screen Space - Camera。
    // 有 Landscape / Portrait 状态的 StateRoot 切到对应状态；弹窗背后没有场景，直接落在桌面色上。
    [MenuItem(CustomEditorMenuPaths.UI + "/生成页面预览截图")]
    public static void CapturePages()
    {
        TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.RegularFontAssetPath);
        Vector2Int landscape = Vector2Int.RoundToInt(UICanvasResolutionProfile.EditorDefaultReferenceResolution);
        Vector2Int portrait = Vector2Int.RoundToInt(UICanvasResolutionProfile.EditorMobilePreviewReferenceResolution);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PageFolder })) {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            RenderPreview($"page_{prefab.name}.png", landscape, (scene, camera) => InstantiatePage(scene, camera, prefab, landscape, "Landscape", regular));
            RenderPreview($"page_{prefab.name}_portrait.png", portrait, (scene, camera) => InstantiatePage(scene, camera, prefab, portrait, "Portrait", regular));
        }
    }

    private static void RenderSheet(string fileName, Action<RectTransform> build)
    {
        RenderPreview(fileName, new Vector2Int(Width, Height), (scene, camera) => build(CreateCanvas(scene, camera)));
    }

    private static void RenderPreview(string fileName, Vector2Int size, Action<Scene, Camera> build)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try {
            Camera camera = CreateCamera(scene, size.y);
            build(scene, camera);
            Canvas.ForceUpdateCanvases();
            Directory.CreateDirectory(OutputFolder);
            string path = Path.Combine(OutputFolder, fileName);
            EditorUtils.RenderCameraToPng(camera, size.x, size.y, MsaaSamples, path);
            Debug.Log($"Theme preview captured to {Path.GetFullPath(path)}");
        }
        finally {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void BuildTokenSheet(RectTransform canvas, TMP_FontAsset regular, FontSample[] fontSamples)
    {
        float cardWidth = (Width - Margin * 3f) * 0.5f;
        RectTransform card = AddImage(canvas, "PaperCard", Margin, Margin, cardWidth, Height - Margin * 2f, UIPalette.Paper).rectTransform;
        BuildTokenColumn(card, regular, fontSamples, UIPalette.Ink, UIPalette.InkSecondary, UIPalette.InkTertiary, CardPadding);

        RectTransform table = AddRect(canvas, "OnTable", Margin * 2f + cardWidth, Margin, cardWidth, Height - Margin * 2f);
        BuildTokenColumn(table, regular, fontSamples, UIPalette.Paper, UIPalette.PaperSunken, UIPalette.InkTertiary, CardPadding);
    }

    // 同一套内容分别放在纸卡上（墨色字）和直接放在桌面上（纸色字），对比两种底色下的可读性。
    private static void BuildTokenColumn(RectTransform parent, TMP_FontAsset font, FontSample[] fontSamples, Color primary, Color secondary, Color tertiary, float padding)
    {
        float y = padding;
        AddText(parent, "UI Theme Preview", padding, y, 28, primary, font);
        y += 56f;

        foreach (int size in FontSizeLadder) {
            AddText(parent, SampleText, padding, y, size, primary, font);
            AddText(parent, size.ToString(), padding + 520f, y + (size - 15) * 0.5f, 15, tertiary, font);
            y += size * 1.45f;
        }

        AddText(parent, "暂无对局记录", padding, y, 18, secondary, font);
        y += 30f;
        AddText(parent, "请选择棋盘", padding, y, 15, tertiary, font);
        y += 36f;

        // 字重对比：Regular Bold 是现有页面的伪粗体，Medium / Serif SemiBold 是 V3 要替换成的真字重。
        foreach (FontSample sample in fontSamples) {
            if (sample.Font == null) {
                continue;
            }

            TextMeshProUGUI label = AddText(parent, SampleText, padding, y, 22, primary, sample.Font);
            if (label != null) {
                label.fontStyle = sample.Style;
            }

            AddText(parent, sample.Label, padding + 520f, y + 3.5f, 15, tertiary, font);
            y += 34f;
        }

        y += 14f;
        int index = 0;
        foreach (FieldInfo field in typeof(UIPalette).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (field.FieldType != typeof(Color)) {
                continue;
            }

            Color color = (Color)field.GetValue(null);
            float x = padding + index % SwatchColumns * SwatchCellWidth;
            float cellY = y + index / SwatchColumns * SwatchCellHeight;
            AddImage(parent, field.Name, x, cellY, SwatchWidth, SwatchHeight, color);
            AddText(parent, field.Name, x, cellY + SwatchHeight + 6f, 15, primary, font);
            AddText(parent, "#" + ColorUtility.ToHtmlStringRGB(color) + (color.a < 1f ? $" {color.a:0.00}" : string.Empty), x, cellY + SwatchHeight + 26f, 15, tertiary, font);
            index++;
        }
    }

    // 纸卡带柔影放在桌面上，控件全部用主题贴图 + 令牌着色。
    private static void BuildControlSheet(RectTransform canvas, TMP_FontAsset regular, TMP_FontAsset medium, TMP_FontAsset serif)
    {
        const float cardX = 80f;
        const float cardY = 60f;
        const float cardWidth = Width - cardX * 2f;
        const float cardHeight = Height - cardY * 2f;
        const float left = 48f;
        float extent = UIThemeTextureTool.CardShadowExtent;
        AddSprite(canvas, UIThemeTextureTool.CardShadow, cardX - extent, cardY - extent + 3f, cardWidth + extent * 2f, cardHeight + extent * 2f, WithAlpha(UIPalette.Ink, 0.8f));
        RectTransform card = AddSprite(canvas, UIThemeTextureTool.Panel, cardX, cardY, cardWidth, cardHeight, UIPalette.Paper).rectTransform;

        AddText(card, "开始对局", left, 36f, 28, UIPalette.Ink, serif);
        AddText(card, "Serif SemiBold 28", left + 136f, 46f, 15, UIPalette.InkTertiary, regular);

        // 按钮：同一张贴图，底色直接取运行时的 ColorTint 色块；按下时文字下沉、禁用时文字变淡，与 UIButtonFeedback 一致。
        ColorBlock paperColors = UIPalette.PaperButtonColors;
        ColorBlock accentColors = UIPalette.AccentButtonColors;
        Color disabledText = WithAlpha(UIPalette.Ink, UIPalette.Ink.a * UIButtonFeedback.DisabledContentAlpha);
        (string label, Color fill, Color text, float sink)[] buttons = {
            ("Primary", accentColors.normalColor, UIPalette.Paper, 0f),
            ("Primary Pressed", accentColors.pressedColor, UIPalette.Paper, UIButtonFeedback.SinkDistance),
            ("Normal", paperColors.normalColor, UIPalette.Ink, 0f),
            ("Hover", paperColors.highlightedColor, UIPalette.Ink, 0f),
            ("Pressed", paperColors.pressedColor, UIPalette.Ink, UIButtonFeedback.SinkDistance),
            ("Disabled", paperColors.disabledColor, disabledText, 0f),
        };
        const float buttonPitch = 204f;
        float y = 110f;
        for (int i = 0; i < buttons.Length; i++) {
            float x = left + i * buttonPitch;
            RectTransform button = AddSprite(card, UIThemeTextureTool.Button, x, y, 180f, 52f, buttons[i].fill).rectTransform;
            AddCenteredText(button, "开始对局", 0f, buttons[i].sink, 180f, 52f, 22, buttons[i].text, medium);
            AddCenteredText(card, buttons[i].label, x, y + 60f, 180f, 20f, 15, UIPalette.InkTertiary, regular);
        }

        float iconButtonX = left + buttons.Length * buttonPitch;
        RectTransform iconButton = AddSprite(card, UIThemeTextureTool.Button, iconButtonX, y, 52f, 52f, paperColors.normalColor).rectTransform;
        AddSprite(iconButton, "icon_close", 14f, 14f, 24f, 24f, UIPalette.Ink);
        AddCenteredText(card, "Icon", iconButtonX, y + 60f, 52f, 20f, 15, UIPalette.InkTertiary, regular);

        // 输入框、下拉、勾选；未勾选框和输入框一样用凹面色，纸色在纸卡上只剩细线，太淡。
        y = 230f;
        RectTransform input = AddSprite(card, UIThemeTextureTool.Input, left, y, 360f, 48f, UIPalette.PaperSunken).rectTransform;
        AddText(input, "请选择棋盘", 16f, 12f, 18, UIPalette.InkTertiary, regular);
        AddText(card, "Input", left, y + 56f, 15, UIPalette.InkTertiary, regular);

        RectTransform dropdown = AddSprite(card, UIThemeTextureTool.Button, 432f, y, 240f, 48f, UIPalette.Paper).rectTransform;
        AddText(dropdown, "19x19", 16f, 12f, 18, UIPalette.Ink, regular);
        AddSprite(dropdown, "icon_down", 240f - 36f, 12f, 24f, 24f, UIPalette.InkSecondary);
        AddText(card, "Dropdown", 432f, y + 56f, 15, UIPalette.InkTertiary, regular);

        AddSprite(card, UIThemeTextureTool.ToggleBox, 696f, y + 12f, 24f, 24f, UIPalette.PaperSunken);
        AddText(card, "复盘", 732f, y + 12f, 18, UIPalette.Ink, regular);
        RectTransform checkedBox = AddSprite(card, UIThemeTextureTool.ToggleBox, 800f, y + 12f, 24f, 24f, UIPalette.Ink).rectTransform;
        AddSprite(checkedBox, "icon_check", 2f, 2f, 20f, 20f, UIPalette.Paper);
        AddText(card, "胜率", 836f, y + 12f, 18, UIPalette.Ink, regular);
        AddText(card, "Toggle", 696f, y + 56f, 15, UIPalette.InkTertiary, regular);

        // 滑条与滚动条共用胶囊贴图。
        y = 350f;
        AddSprite(card, UIThemeTextureTool.Capsule, left, y + 14f, 360f, 4f, UIPalette.Hairline);
        AddSprite(card, UIThemeTextureTool.Capsule, left, y + 14f, 216f, 4f, UIPalette.InkSecondary);
        AddSprite(card, UIThemeTextureTool.Knob, left + 216f - 16f, y, 32f, 32f, UIPalette.Paper);
        AddText(card, "Slider", left, y + 40f, 15, UIPalette.InkTertiary, regular);

        AddSprite(card, UIThemeTextureTool.Capsule, 432f, y + 12f, 240f, 8f, UIPalette.Hairline);
        AddSprite(card, UIThemeTextureTool.Capsule, 492f, y + 12f, 96f, 8f, UIPalette.InkTertiary);
        AddText(card, "Scrollbar", 432f, y + 40f, 15, UIPalette.InkTertiary, regular);

        // 列表：分组底 PaperSunken，选中项 PaperRaised，行间 1px 细线用无 sprite 的 Image。
        y = 430f;
        RectTransform list = AddSprite(card, UIThemeTextureTool.Panel, left, y, 480f, 156f, UIPalette.PaperSunken).rectTransform;
        AddSprite(list, UIThemeTextureTool.Button, 6f, 6f, 468f, 40f, UIPalette.PaperRaised);
        string[] rows = { "开始对局", "复盘", "胜率" };
        for (int i = 0; i < rows.Length; i++) {
            AddText(list, rows[i], 20f, i * 52f + 15f, 18, UIPalette.Ink, regular);
            AddText(list, "12:34", 400f, i * 52f + 15f, 18, UIPalette.InkSecondary, regular);
            if (i > 0) {
                AddImage(list, "Divider", 16f, i * 52f, 448f, 1f, UIPalette.Hairline);
            }
        }

        AddText(card, "List", left, y + 164f, 15, UIPalette.InkTertiary, regular);

        // 图标：32 和 20 两个尺寸，检查 mipmap 缩小后的线宽。
        y = 640f;
        for (int i = 0; i < UIThemeTextureTool.IconNames.Length; i++) {
            string iconName = UIThemeTextureTool.IconNames[i];
            float x = left + i * 100f;
            AddSprite(card, iconName, x + 34f, y, 32f, 32f, UIPalette.Ink);
            AddSprite(card, iconName, x + 40f, y + 42f, 20f, 20f, UIPalette.InkSecondary);
            AddCenteredText(card, iconName.Substring("icon_".Length), x, y + 70f, 100f, 20f, 15, UIPalette.InkTertiary, regular);
        }
    }

    // 先挂在未激活的父节点下实例化，TMP 生成网格之前清掉含 Regular 图集缺字的文字，避免编辑态给 Regular 追加字形。
    private static void InstantiatePage(Scene scene, Camera camera, GameObject prefab, Vector2Int size, string platformState, TMP_FontAsset regular)
    {
        GameObject holder = new GameObject("PageHolder");
        holder.SetActive(false);
        UnitySceneManager.MoveGameObjectToScene(holder, scene);
        GameObject page = UnityEngine.Object.Instantiate(prefab, holder.transform);

        int cleared = 0;
        foreach (TMP_Text text in page.GetComponentsInChildren<TMP_Text>(true)) {
            if ((text.font == null || text.font == regular) && FindMissingCharacter(regular, text.text) != '\0') {
                text.text = string.Empty;
                cleared++;
            }
        }

        if (cleared > 0) {
            Debug.LogWarning($"Page preview {prefab.name}: cleared {cleared} texts with characters not in the {regular.name} atlas yet.");
        }

        foreach (StateRoot stateRoot in page.GetComponentsInChildren<StateRoot>(true)) {
            foreach (StateConfig state in stateRoot.States) {
                if (state.name == platformState) {
                    stateRoot.SetState(platformState, true);
                    break;
                }
            }
        }

        Canvas canvas = page.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        RectTransform rectTransform = (RectTransform)page.transform;
        rectTransform.SetParent(null, false);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.position = Vector3.zero;
    }

    private static Camera CreateCamera(Scene scene, int height)
    {
        GameObject cameraObject = new GameObject("ThemePreviewCamera");
        UnitySceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.scene = scene;
        camera.orthographic = true;
        camera.orthographicSize = height * 0.5f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = UIPalette.Table;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        return camera;
    }

    private static RectTransform CreateCanvas(Scene scene, Camera camera)
    {
        GameObject canvasObject = new GameObject("ThemePreviewCanvas", typeof(RectTransform));
        UnitySceneManager.MoveGameObjectToScene(canvasObject, scene);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        RectTransform rectTransform = (RectTransform)canvasObject.transform;
        rectTransform.sizeDelta = new Vector2(Width, Height);
        rectTransform.position = Vector3.zero;
        return rectTransform;
    }

    // 以父节点左上角为原点、向下为正的像素坐标摆放，样张只需要固定排版。
    private static RectTransform AddRect(RectTransform parent, string name, float x, float y, float width, float height)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rectTransform = (RectTransform)gameObject.transform;
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(x, -y);
        rectTransform.sizeDelta = new Vector2(width, height);
        return rectTransform;
    }

    private static Image AddImage(RectTransform parent, string name, float x, float y, float width, float height, Color color)
    {
        Image image = AddRect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    // 有九宫格边框的主题贴图按 Sliced 绘制，图标按 Simple 绘制；贴图缺失时退回纯色块并警告。
    private static Image AddSprite(RectTransform parent, string spriteName, float x, float y, float width, float height, Color color)
    {
        Image image = AddImage(parent, spriteName, x, y, width, height, color);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UIThemeTextureTool.SpritePath(spriteName));
        if (sprite == null) {
            Debug.LogWarning($"Theme sprite not found: {spriteName}, run {CustomEditorMenuPaths.UI}/生成主题贴图 first.");
            return image;
        }

        image.sprite = sprite;
        image.type = sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
        return image;
    }

    private static TextMeshProUGUI AddText(RectTransform parent, string text, float x, float y, float fontSize, Color color, TMP_FontAsset font)
    {
        return AddText(parent, text, x, y, 600f, fontSize * 1.4f, fontSize, color, font, TextAlignmentOptions.TopLeft);
    }

    private static TextMeshProUGUI AddCenteredText(RectTransform parent, string text, float x, float y, float width, float height, float fontSize, Color color, TMP_FontAsset font)
    {
        return AddText(parent, text, x, y, width, height, fontSize, color, font, TextAlignmentOptions.Center);
    }

    private static TextMeshProUGUI AddText(RectTransform parent, string text, float x, float y, float width, float height, float fontSize, Color color, TMP_FontAsset font, TextAlignmentOptions alignment)
    {
        if (font == glyphFrozenFont && !HasAtlasCharacters(font, text)) {
            return null;
        }

        TextMeshProUGUI label = AddRect(parent, "Text", x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = fontSize;
        label.color = color;
        label.text = text;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    private static bool HasAtlasCharacters(TMP_FontAsset font, string text)
    {
        char missing = FindMissingCharacter(font, text);
        if (missing != '\0') {
            Debug.LogWarning($"Theme preview skipped text \"{text}\": '{missing}' is not in the {font.name} atlas yet.");
            return false;
        }

        return true;
    }

    // 换行等控制字符不走图集，不算缺字。
    private static char FindMissingCharacter(TMP_FontAsset font, string text)
    {
        foreach (char character in text) {
            if (character >= ' ' && !font.characterLookupTable.ContainsKey(character)) {
                return character;
            }
        }

        return '\0';
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
#endif
