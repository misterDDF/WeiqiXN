#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// V3 主题迁移：按页面规则表把 prefab 节点换成主题样式，可重复执行。
// 规则按节点路径指定样式角色，不按旧颜色全局替换：同一种深色在 HUD 里要换成纸色，在遮罩里仍是深色，只有节点所在位置能决定角色。
// 只写外观字段：Image 的 sprite / type / color，TMP 的字体、字号、颜色和粗体，Selectable 的 ColorTint 色块；
// 另外给按钮挂 UIButtonFeedback，并移除被套样式节点上的 Shadow / Outline。
// 不碰 RectTransform、LayoutGroup 和 Canvas：保存前把全部 RectTransform 和根 Canvas 渲染模式恢复成 prefab 资产里的值。
public static class UIThemeMigrationTool
{
    private const string PageFolder = "Assets/UI/Prefab/Page/";

    private enum ThemeFont
    {
        Regular,
        Medium,
        Serif,
    }

    // Sprite 为空表示无 sprite 的纯色块。
    private readonly struct ImageStyle
    {
        public readonly string Sprite;
        public readonly Color Color;

        public ImageStyle(string sprite, Color color)
        {
            Sprite = sprite;
            Color = color;
        }
    }

    private readonly struct TextStyle
    {
        public readonly ThemeFont Font;
        public readonly float Size;
        public readonly Color Color;

        public TextStyle(ThemeFont font, float size, Color color)
        {
            Font = font;
            Size = size;
            Color = color;
        }

        public TextStyle WithSize(float size)
        {
            return new TextStyle(Font, size, Color);
        }
    }

    // 底板 Image.color 置白，底色由色块给出（见 UIPalette.PaperButtonColors 的注释）。
    private readonly struct SelectableStyle
    {
        public readonly string Plate;
        public readonly ColorBlock Colors;
        public readonly bool Feedback;

        public SelectableStyle(string plate, ColorBlock colors, bool feedback)
        {
            Plate = plate;
            Colors = colors;
            Feedback = feedback;
        }
    }

    private readonly struct Rule
    {
        public readonly string Path;
        public readonly Action<GameObject> Apply;

        public Rule(string path, Action<GameObject> apply)
        {
            Path = path;
            Apply = apply;
        }
    }

    private readonly struct ThemePage
    {
        public readonly string Name;
        public readonly Rule[] Rules;

        public ThemePage(string name, params Rule[] rules)
        {
            Name = name;
            Rules = rules;
        }
    }

    // 图片角色。Clear 用于迁移后不再需要的装饰节点（旧标题栏、描金线），保留节点以免改动布局。
    private static readonly ImageStyle Desk = new ImageStyle(null, UIPalette.Table);
    private static readonly ImageStyle Scrim = new ImageStyle(null, WithAlpha(UIPalette.Table, 0.72f));
    private static readonly ImageStyle Card = new ImageStyle(UIThemeTextureTool.Panel, UIPalette.Paper);
    private static readonly ImageStyle Clear = new ImageStyle(null, Color.clear);
    private static readonly ImageStyle ProgressTrack = new ImageStyle(UIThemeTextureTool.Capsule, UIPalette.PaperSunken);
    private static readonly ImageStyle ProgressFill = new ImageStyle(UIThemeTextureTool.Capsule, UIPalette.InkSecondary);

    // 文字角色，字号取字号阶梯 40 / 28 / 22 / 18 / 15。
    private static readonly TextStyle Heading = new TextStyle(ThemeFont.Serif, 28f, UIPalette.Ink);
    private static readonly TextStyle Emphasis = new TextStyle(ThemeFont.Medium, 22f, UIPalette.Ink);
    private static readonly TextStyle Body = new TextStyle(ThemeFont.Regular, 22f, UIPalette.Ink);
    private static readonly TextStyle Placeholder = new TextStyle(ThemeFont.Regular, 22f, UIPalette.InkTertiary);
    private static readonly TextStyle Detail = new TextStyle(ThemeFont.Regular, 18f, UIPalette.InkSecondary);
    private static readonly TextStyle Caption = new TextStyle(ThemeFont.Regular, 15f, UIPalette.InkSecondary);
    private static readonly TextStyle ButtonLabel = new TextStyle(ThemeFont.Medium, 22f, UIPalette.Ink);
    private static readonly TextStyle AccentButtonLabel = new TextStyle(ThemeFont.Medium, 22f, UIPalette.Paper);

    // 可交互控件。按钮挂 UIButtonFeedback；输入框按下时子节点下沉没有意义，不挂。
    private static readonly SelectableStyle PaperButton = new SelectableStyle(UIThemeTextureTool.Button, UIPalette.PaperButtonColors, true);
    private static readonly SelectableStyle AccentButton = new SelectableStyle(UIThemeTextureTool.Button, UIPalette.AccentButtonColors, true);
    private static readonly SelectableStyle InputBox = new SelectableStyle(UIThemeTextureTool.Input, UIPalette.InputColors, false);

    // 页面规则表，路径相对 prefab 根节点；V3.3 按页面顺序逐个补充。
    private static readonly ThemePage[] Pages = {
        new ThemePage("LoadingPage",
            ImageRule("bg", Desk),
            ImageRule("bg/panel_status", Card),
            TextRule("bg/panel_status/txt_loading", Emphasis.WithSize(28f)),
            TextRule("bg/panel_status/txt_detail", Detail),
            ImageRule("bg/panel_status/img_progress_track", ProgressTrack),
            ImageRule("bg/panel_status/img_progress_track/img_progress_fill", ProgressFill),
            TextRule("bg/panel_status/txt_percent", Caption)),
        new ThemePage("ConfirmPopup",
            ImageRule("mask", Scrim),
            ImageRule("panel_main", Card),
            ImageRule("panel_main/img_header", Clear),
            ImageRule("panel_main/img_accent_line", Clear),
            ImageRule("panel_main/img_tip_icon", new ImageStyle("icon_info", UIPalette.InkSecondary)),
            TextRule("panel_main/txt_title", Heading),
            TextRule("panel_main/txt_content", Body),
            SelectableRule("panel_main/input_content", InputBox),
            TextRule("panel_main/input_content/Text", Body),
            TextRule("panel_main/input_content/Placeholder", Placeholder),
            SelectableRule("panel_main/btn_cancel", PaperButton),
            TextRule("panel_main/btn_cancel/txt_cancel", ButtonLabel),
            SelectableRule("panel_main/btn_confirm", AccentButton),
            TextRule("panel_main/btn_confirm/txt_confirm", AccentButtonLabel)),
    };

    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<ThemeFont, TMP_FontAsset> fontCache = new Dictionary<ThemeFont, TMP_FontAsset>();
    private static int warningCount;

    private readonly struct RectState
    {
        private readonly RectTransform target;
        private readonly Vector2 anchorMin;
        private readonly Vector2 anchorMax;
        private readonly Vector2 pivot;
        private readonly Vector2 sizeDelta;
        private readonly Vector3 anchoredPosition;
        private readonly Quaternion localRotation;
        private readonly Vector3 localScale;

        public RectState(RectTransform target, RectTransform source)
        {
            this.target = target;
            anchorMin = source.anchorMin;
            anchorMax = source.anchorMax;
            pivot = source.pivot;
            sizeDelta = source.sizeDelta;
            anchoredPosition = source.anchoredPosition3D;
            localRotation = source.localRotation;
            localScale = source.localScale;
        }

        public void Restore()
        {
            target.anchorMin = anchorMin;
            target.anchorMax = anchorMax;
            target.pivot = pivot;
            target.sizeDelta = sizeDelta;
            target.anchoredPosition3D = anchoredPosition;
            target.localRotation = localRotation;
            target.localScale = localScale;
        }
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/应用主题迁移")]
    public static void Apply()
    {
        spriteCache.Clear();
        fontCache.Clear();
        warningCount = 0;
        if (!LoadThemeAssets()) {
            return;
        }

        int changed = 0;
        foreach (ThemePage page in Pages) {
            if (ApplyPage(page)) {
                changed++;
            }
        }

        Debug.Log($"Theme migration checked. pages: {Pages.Length}, changed: {changed}, warnings: {warningCount}");
    }

    private static Rule ImageRule(string path, ImageStyle style)
    {
        return new Rule(path, target => ApplyImage(target, style));
    }

    private static Rule TextRule(string path, TextStyle style)
    {
        return new Rule(path, target => ApplyText(target, style));
    }

    private static Rule SelectableRule(string path, SelectableStyle style)
    {
        return new Rule(path, target => ApplySelectable(target, style));
    }

    // 主题贴图和字体缺一个就不迁移，避免半套样式写进 prefab。
    private static bool LoadThemeAssets()
    {
        List<string> spriteNames = new List<string>(UIThemeTextureTool.IconNames) {
            UIThemeTextureTool.Panel, UIThemeTextureTool.CardShadow, UIThemeTextureTool.Button, UIThemeTextureTool.Input,
            UIThemeTextureTool.ToggleBox, UIThemeTextureTool.Capsule, UIThemeTextureTool.Knob,
        };
        foreach (string spriteName in spriteNames) {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UIThemeTextureTool.SpritePath(spriteName));
            if (sprite == null) {
                Debug.LogError($"Theme sprite not found: {spriteName}, run {CustomEditorMenuPaths.UI}/生成主题贴图 first.");
                return false;
            }

            spriteCache[spriteName] = sprite;
        }

        (ThemeFont font, string path)[] fonts = {
            (ThemeFont.Regular, UIThemeFontAssetTool.RegularFontAssetPath),
            (ThemeFont.Medium, UIThemeFontAssetTool.MediumFontAssetPath),
            (ThemeFont.Serif, UIThemeFontAssetTool.SerifFontAssetPath),
        };
        foreach ((ThemeFont font, string path) in fonts) {
            TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset == null) {
                Debug.LogError($"Theme font not found: {path}, run {CustomEditorMenuPaths.UI}/生成主题字体资产 first.");
                return false;
            }

            fontCache[font] = asset;
        }

        return true;
    }

    private static bool ApplyPage(ThemePage page)
    {
        string path = PageFolder + page.Name + ".prefab";
        if (!File.Exists(path)) {
            Warn($"Theme page not found: {path}");
            return false;
        }

        string before = File.ReadAllText(path);
        // 恢复值取自 prefab 资产本身：打开后的根节点已被 Canvas 驱动成预览尺寸，不再是零尺寸的编辑态结构。
        // 规则只增删组件不增删节点，资产与打开内容的 RectTransform 按遍历顺序一一对应。
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        RectTransform[] sourceRects = asset.GetComponentsInChildren<RectTransform>(true);
        Canvas assetCanvas = asset.GetComponent<Canvas>();
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try {
            RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
            if (rects.Length != sourceRects.Length) {
                Warn($"Theme page {page.Name}: RectTransform count mismatch between asset and loaded contents, skipped.");
                return false;
            }

            RectState[] rectStates = new RectState[rects.Length];
            for (int i = 0; i < rects.Length; i++) {
                rectStates[i] = new RectState(rects[i], sourceRects[i]);
            }

            Canvas rootCanvas = root.GetComponent<Canvas>();
            RenderMode rootRenderMode = assetCanvas != null ? assetCanvas.renderMode : RenderMode.ScreenSpaceCamera;

            foreach (Rule rule in page.Rules) {
                Transform node = root.transform.Find(rule.Path);
                if (node == null) {
                    Warn($"Theme rule target not found: {page.Name}/{rule.Path}");
                    continue;
                }

                // 嵌套 widget 在它自己的 prefab 里迁移，不在页面里写 override。
                if (PrefabUtility.IsPartOfPrefabInstance(node.gameObject)) {
                    Warn($"Theme rule target is inside a nested prefab, migrate the widget prefab instead: {page.Name}/{rule.Path}");
                    continue;
                }

                rule.Apply(node.gameObject);
            }

            foreach (RectState state in rectStates) {
                state.Restore();
            }

            if (rootCanvas != null) {
                rootCanvas.renderMode = rootRenderMode;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }

        bool changed = File.ReadAllText(path) != before;
        Debug.Log($"Theme page {page.Name}: rules {page.Rules.Length}, changed: {changed}");
        return changed;
    }

    private static void ApplyImage(GameObject target, ImageStyle style)
    {
        Image image = target.GetComponent<Image>();
        if (image == null) {
            Warn($"Theme image rule needs an Image: {target.name}");
            return;
        }

        SetSprite(image, style.Sprite);
        image.color = style.Color;
        RemoveMeshEffects(target);
    }

    private static void ApplyText(GameObject target, TextStyle style)
    {
        TextMeshProUGUI text = target.GetComponent<TextMeshProUGUI>();
        if (text == null) {
            Warn($"Theme text rule needs a TextMeshProUGUI: {target.name}");
            return;
        }

        TMP_FontAsset font = fontCache[style.Font];
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontStyle &= ~FontStyles.Bold;
        text.fontSize = style.Size;
        // 自动字号的文字以样式字号为上限，下限不高于上限。
        if (text.enableAutoSizing) {
            text.fontSizeMax = style.Size;
            text.fontSizeMin = Mathf.Min(text.fontSizeMin, style.Size);
        }

        text.color = style.Color;
        RemoveMeshEffects(target);
    }

    private static void ApplySelectable(GameObject target, SelectableStyle style)
    {
        Selectable selectable = target.GetComponent<Selectable>();
        Image plate = selectable != null ? selectable.targetGraphic as Image : null;
        if (plate == null) {
            Warn($"Theme selectable rule needs a Selectable with an Image target graphic: {target.name}");
            return;
        }

        SetSprite(plate, style.Plate);
        plate.color = Color.white;
        RemoveMeshEffects(plate.gameObject);
        selectable.transition = Selectable.Transition.ColorTint;
        selectable.colors = style.Colors;
        if (style.Feedback && target.GetComponent<UIButtonFeedback>() == null) {
            target.AddComponent<UIButtonFeedback>();
        }
    }

    // 有九宫格边框的主题贴图按 Sliced 绘制，图标按 Simple 并保持比例。
    private static void SetSprite(Image image, string spriteName)
    {
        Sprite sprite = spriteName != null ? spriteCache[spriteName] : null;
        bool sliced = sprite != null && sprite.border != Vector4.zero;
        image.sprite = sprite;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = 1f;
        image.fillCenter = true;
        image.preserveAspect = sprite != null && !sliced;
    }

    // 纸卡上的文字和底板不需要旧皮肤的描边与投影。Outline 继承自 Shadow。
    private static void RemoveMeshEffects(GameObject target)
    {
        foreach (Shadow effect in target.GetComponents<Shadow>()) {
            UnityEngine.Object.DestroyImmediate(effect);
        }
    }

    private static void Warn(string message)
    {
        warningCount++;
        Debug.LogWarning(message);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
#endif
