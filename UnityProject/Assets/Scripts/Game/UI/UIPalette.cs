using UnityEngine;
using UnityEngine.UI;

// UI 色板令牌，色值与 modules/14-visual-polish-plan.md「设计令牌 / 色板」一致；prefab 侧颜色迁移以同一色值为准。
public static class UIPalette
{
    public static readonly Color Ink = Rgb(0x1F1C19);
    public static readonly Color InkSecondary = Rgb(0x5C564E);
    public static readonly Color InkTertiary = Rgb(0x948C80);
    public static readonly Color Hairline = Rgb(0x1F1C19, 0.14f);
    public static readonly Color Paper = Rgb(0xF3EFE6);
    public static readonly Color PaperSunken = Rgb(0xE8E2D5);
    public static readonly Color PaperRaised = Rgb(0xFBF9F4);
    public static readonly Color Table = Rgb(0x35302A);
    public static readonly Color Kaya = Rgb(0xD9BC87);
    public static readonly Color Accent = Rgb(0xA8432F);
    public static readonly Color Analysis = Rgb(0x3F605B);
    public static readonly Color Hint = Rgb(0x3CB95A);
    public static readonly Color HintOutline = Rgb(0x25843F);
    public static readonly Color Positive = Rgb(0x4F6B3A);
    public static readonly Color Negative = Rgb(0xA8432F);
    // 深色棋桌上的墨色卡（黑方玩家卡）、抽屉下的遮罩、墨底上提亮的朱色。
    public static readonly Color InkCard = Rgb(0x12100E, 0.86f);
    public static readonly Color Scrim = Rgb(0x0E0C0A, 0.45f);
    public static readonly Color AccentOnInk = Rgb(0xCC6048);

    // 按钮禁用时底板的不透明度；文字和图标由 UIButtonFeedback 另行降低不透明度。
    public const float DisabledAlpha = 0.6f;

    // 按钮 ColorTint 色块。ColorTint 是乘法，只能压暗不能提亮，所以按钮底板 Image.color 保持白色，底色由色块给出。
    // 选中色等于常态，避免桌面端点击后悬停色一直“粘住”。朱色按钮的悬停和按下由 Accent 推算，不另设令牌。
    public static readonly ColorBlock PaperButtonColors = ButtonColors(Paper, PaperRaised, PaperSunken);
    public static readonly ColorBlock AccentButtonColors = ButtonColors(Accent, Color.Lerp(Accent, Color.white, 0.08f), Scale(Accent, 0.88f));
    // 输入框底板同样置白，常态为凹面色，悬停向纸色提亮一半。
    public static readonly ColorBlock InputColors = ButtonColors(PaperSunken, Color.Lerp(PaperSunken, Paper, 0.5f), PaperSunken);

    private static ColorBlock ButtonColors(Color normal, Color highlighted, Color pressed)
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = normal;
        colors.highlightedColor = highlighted;
        colors.pressedColor = pressed;
        colors.selectedColor = normal;
        colors.disabledColor = new Color(normal.r, normal.g, normal.b, DisabledAlpha);
        return colors;
    }

    private static Color Scale(Color color, float factor)
    {
        return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
    }

    private static Color Rgb(int rgb, float alpha = 1f)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }
}
