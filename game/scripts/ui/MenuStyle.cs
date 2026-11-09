using Godot;

namespace VioVau.Game.UI;

/// <summary>The menu's look: violet panels, bordered buttons that light up on hover, dark inputs.</summary>
public static class MenuStyle
{
    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = 10, DefaultFont = UiFonts.Regular };

        theme.SetStylebox("normal", "Button", Box(new Color("1d1030"), Palette.Wall));
        theme.SetStylebox("hover", "Button", Box(new Color("2a1a45"), Palette.Accent));
        theme.SetStylebox("pressed", "Button", Box(new Color("4b2a7a"), Palette.White));
        theme.SetStylebox("disabled", "Button", Box(new Color(0.08f, 0.05f, 0.12f, 0.8f), new Color("241439")));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", Palette.White);
        theme.SetColor("font_hover_color", "Button", Palette.White);
        theme.SetColor("font_pressed_color", "Button", Palette.White);
        theme.SetColor("font_disabled_color", "Button", Palette.Wall);
        theme.SetFontSize("font_size", "Button", 11);

        theme.SetStylebox("normal", "LineEdit", Box(new Color("0d0716"), Palette.Wall));
        theme.SetStylebox("focus", "LineEdit", Box(new Color("0d0716"), Palette.Accent));
        theme.SetStylebox("read_only", "LineEdit", Box(new Color(0.05f, 0.03f, 0.09f), new Color("241439")));
        theme.SetColor("font_color", "LineEdit", Palette.White);
        theme.SetColor("font_placeholder_color", "LineEdit", Palette.Dim);
        theme.SetColor("caret_color", "LineEdit", Palette.White);
        theme.SetFontSize("font_size", "LineEdit", 10);

        theme.SetStylebox("panel", "PanelContainer", Box(new Color(0.07f, 0.04f, 0.12f, 0.96f), Palette.Wall, padding: 10));

        // Sliders: visible track + grabber on the dark violet panels.
        theme.SetStylebox("slider", "HSlider", new StyleBoxFlat
        {
            BgColor = new Color("0d0716"),
            BorderColor = Palette.Wall,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginTop = 4, ContentMarginBottom = 4,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        });
        theme.SetStylebox("grabber_area", "HSlider", new StyleBoxFlat { BgColor = Palette.Accent });
        theme.SetStylebox("grabber_area_highlight", "HSlider", new StyleBoxFlat { BgColor = Palette.White });
        theme.SetConstant("center_grabber", "HSlider", 1);
        return theme;
    }

    public static StyleBoxFlat Box(Color background, Color border, int padding = 6) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2,
        CornerRadiusTopRight = 2,
        CornerRadiusBottomLeft = 2,
        CornerRadiusBottomRight = 2,
        ContentMarginLeft = padding + 2,
        ContentMarginRight = padding + 2,
        ContentMarginTop = padding / 2f + 1,
        ContentMarginBottom = padding / 2f + 1,
    };
}
