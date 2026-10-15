using Microsoft.Win32;
using System.Windows.Media;
using TaskTodo.Core.Models;
using Color = System.Windows.Media.Color;

namespace TaskTodo.App.Desktop;

internal static class ThemeService
{
    // Each palette is a tinted neutral ramp plus one accent. AccentText is the accent shade that stays legible as text;
    // On is the foreground used on a filled accent (check marks, primary buttons).
    private sealed record Tones(string Surface, string Border, string Text, string TextSecondary, string TextTertiary,
        string Hover, string Field, string Accent, string AccentText, string On);

    private static readonly Dictionary<ColorPalette, (Tones Light, Tones Dark)> Palettes = new()
    {
        [ColorPalette.Indigo] = (
            new("#FAFAFC", "#E4E4EB", "#1C1C28", "#5E5E70", "#8E8EA0", "#EEEEF4", "#F0F0F5", "#5B5BD6", "#4C4CC2", "#FFFFFF"),
            new("#1F1F27", "#34343F", "#F1F1F6", "#BDBDCC", "#868698", "#2A2A34", "#282832", "#9B9BFF", "#AEAEFF", "#17173D")),
        [ColorPalette.Coral] = (
            new("#FBFAF7", "#E8E3DB", "#2B2724", "#6E655D", "#9C9389", "#F1EDE6", "#F3EFE9", "#E0663A", "#B9512A", "#FFFFFF"),
            new("#262321", "#3B3632", "#F2EEE9", "#BDB4AA", "#8C8379", "#322E2A", "#2F2B28", "#F28B5B", "#F7A27A", "#3A1305")),
        [ColorPalette.Rose] = (
            new("#FCFAFA", "#EDE3E5", "#2A2023", "#6F5F64", "#9E8E93", "#F4ECEE", "#F5EFF0", "#D9466F", "#BE3860", "#FFFFFF"),
            new("#261F21", "#3C3235", "#F4ECEE", "#C4B3B8", "#8F7F84", "#322A2D", "#2E272A", "#FF7A9C", "#FF97B2", "#3D0A1A")),
        [ColorPalette.Amber] = (
            new("#FFFFFF", "#E6E6E6", "#111111", "#555555", "#8A8A8A", "#F2F2F2", "#F4F4F4", "#E89B0C", "#9A6200", "#2A1A00"),
            new("#161616", "#2C2C2C", "#F5F5F5", "#BBBBBB", "#7E7E7E", "#222222", "#1F1F1F", "#FFC23D", "#FFC23D", "#2A1A00")),
    };

    public static bool IsSystemDark()
        => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is 0;

    public static void Apply(ThemePreference preference, ColorPalette palette = ColorPalette.Indigo,
        SurfaceMaterial material = SurfaceMaterial.Solid)
    {
        var dark = preference == ThemePreference.Dark || (preference == ThemePreference.System && IsSystemDark());
        var glass = material != SurfaceMaterial.Solid;
        var tones = dark ? Palettes[palette].Dark : Palettes[palette].Light;
        var surface = Parse(tones.Surface);
        var text = Parse(tones.Text);
        var accent = Parse(tones.Accent);
        var resources = System.Windows.Application.Current.Resources;

        resources["SurfaceColor"] = surface;
        SetBrush("TextPrimaryBrush", text);
        // On glass the desktop shows through, so secondary text moves toward the primary tone to keep its contrast.
        SetBrush("TextSecondaryBrush", glass ? Blend(Parse(tones.TextSecondary), text, 0.3) : Parse(tones.TextSecondary));
        SetBrush("TextTertiaryBrush", glass ? Blend(Parse(tones.TextTertiary), text, 0.35) : Parse(tones.TextTertiary));
        SetBrush("AccentBrush", accent);
        SetBrush("AccentTextBrush", Parse(tones.AccentText));
        SetBrush("AccentForegroundBrush", Parse(tones.On));
        SetBrush("AccentSoftBrush", glass ? WithAlpha(accent, (byte)(dark ? 0x40 : 0x33)) : Blend(accent, surface, dark ? 0.22 : 0.13));
        SetBrush("PopupSurfaceBrush", Blend(Colors.White, surface, dark ? 0.06 : 0.6));
        SetBrush("ErrorSurfaceBrush", Blend(Parse("#E5484D"), surface, dark ? 0.24 : 0.12));
        SetBrush("ErrorTextBrush", Parse(dark ? "#FF9592" : "#C62A2F"));

        if (glass)
        {
            SetBrush("BorderBrush", Parse(dark ? "#33FFFFFF" : "#8CFFFFFF"));
            SetBrush("SubtleBorderBrush", Parse(dark ? "#1FFFFFFF" : "#66FFFFFF"));
            SetBrush("ControlHoverBrush", Parse(dark ? "#24FFFFFF" : "#80FFFFFF"));
            SetBrush("ControlSurfaceBrush", Parse(dark ? "#17FFFFFF" : "#59FFFFFF"));
            SetBrush("RowHoverBrush", Parse(dark ? "#1CFFFFFF" : "#6BFFFFFF"));
            SetBrush("TrackBrush", Parse(dark ? "#2EFFFFFF" : "#21000000"));
            SetGradient("GlassEdgeBrush", new(0, 0), new(0.4, 1),
                dark ? [("#4DFFFFFF", 0.0), ("#1AFFFFFF", 0.6), ("#14FFFFFF", 1.0)]
                     : [("#E6FFFFFF", 0.0), ("#73FFFFFF", 0.6), ("#59FFFFFF", 1.0)]);
            SetGradient("GlassSheenBrush", new(0, 0), new(0, 1),
                dark ? [("#14FFFFFF", 0.0), ("#00FFFFFF", 0.4)] : [("#4DFFFFFF", 0.0), ("#00FFFFFF", 0.4)]);
        }
        else
        {
            var border = Parse(tones.Border);
            SetBrush("BorderBrush", border);
            SetBrush("SubtleBorderBrush", Blend(border, surface, 0.55));
            SetBrush("ControlHoverBrush", Parse(tones.Hover));
            SetBrush("ControlSurfaceBrush", Parse(tones.Field));
            SetBrush("RowHoverBrush", Parse(tones.Hover));
            SetBrush("TrackBrush", border);
            SetBrush("GlassEdgeBrush", border);
            SetBrush("GlassSheenBrush", Colors.Transparent);
        }

        void SetGradient(string name, System.Windows.Point start, System.Windows.Point end, (string Color, double Offset)[] stops)
        {
            var brush = new LinearGradientBrush(
                new GradientStopCollection(stops.Select(stop => new GradientStop(Parse(stop.Color), stop.Offset))))
            { StartPoint = start, EndPoint = end };
            brush.Freeze();
            resources[name] = brush;
        }

        void SetBrush(string name, Color value)
        {
            var brush = new SolidColorBrush(value);
            brush.Freeze();
            resources[name] = brush;
        }
    }

    // Opaque mix of `color` over `background`; amount is the share of `color`.
    private static Color Blend(Color color, Color background, double amount)
    {
        byte Mix(byte value, byte under) => (byte)Math.Round(value * amount + under * (1 - amount));
        return Color.FromRgb(Mix(color.R, background.R), Mix(color.G, background.G), Mix(color.B, background.B));
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Parse(string value) => (Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
}
