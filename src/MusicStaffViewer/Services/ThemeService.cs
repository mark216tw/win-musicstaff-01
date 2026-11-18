using Com.Example.MusicStaffViewer.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Com.Example.MusicStaffViewer.Services;

public sealed class ThemeService
{
    private readonly record struct ThemeColors(
        uint Background, uint Sidebar, uint Surface, uint Foreground,
        uint Secondary, uint Accent, uint Border);

    private static readonly Dictionary<(AccentTheme Theme, bool Dark), ThemeColors> Palettes = new()
    {
        [(AccentTheme.EyeCareYellow, false)] = new(0xFFFFF8E0, 0xFFFFF1C4, 0xFFFFFDF3, 0xFF3E3217, 0xFF766A4C, 0xFFB07816, 0xFFE8D795),
        [(AccentTheme.EyeCareYellow, true)] = new(0xFF242015, 0xFF292316, 0xFF30291B, 0xFFF8EDCA, 0xFFD4C395, 0xFFE0B85C, 0xFF5B4B2A),
        [(AccentTheme.ClassicBlue, false)] = new(0xFFF5F8FC, 0xFFF0F5FC, 0xFFFFFFFF, 0xFF1B2A3A, 0xFF56687B, 0xFF2776C6, 0xFFD7E0EA),
        [(AccentTheme.ClassicBlue, true)] = new(0xFF101821, 0xFF162331, 0xFF1D2B39, 0xFFF0F5FA, 0xFFB4C1CF, 0xFF73B4F5, 0xFF35495C),
        [(AccentTheme.ForestGreen, false)] = new(0xFFF2F8F3, 0xFFE7F2E8, 0xFFFFFFFF, 0xFF20382A, 0xFF557460, 0xFF2B8150, 0xFFCEDFD2),
        [(AccentTheme.ForestGreen, true)] = new(0xFF121E17, 0xFF19291E, 0xFF203426, 0xFFEAF6ED, 0xFFACCBB4, 0xFF79C99A, 0xFF3C5A45),
        [(AccentTheme.TwilightPurple, false)] = new(0xFFF8F4FA, 0xFFF0E8F5, 0xFFFFFFFF, 0xFF34213F, 0xFF786383, 0xFF8851AA, 0xFFE2D3EA),
        [(AccentTheme.TwilightPurple, true)] = new(0xFF1B1423, 0xFF281D32, 0xFF35263F, 0xFFF5EDFA, 0xFFD0BEDA, 0xFFCBA1E8, 0xFF594565),
        [(AccentTheme.NeutralGray, false)] = new(0xFFF5F5F5, 0xFFECECEC, 0xFFFFFFFF, 0xFF222222, 0xFF606060, 0xFF5D6872, 0xFFD6D6D6),
        [(AccentTheme.NeutralGray, true)] = new(0xFF171717, 0xFF222222, 0xFF2B2B2B, 0xFFF2F2F2, 0xFFB8B8B8, 0xFFB6C1C9, 0xFF4A4A4A),
    };

    public void Apply(AppSettings settings, FrameworkElement root)
    {
        bool dark = settings.DisplayMode switch
        {
            DisplayMode.Light => false,
            DisplayMode.Dark => true,
            _ => App.IsSystemDark
        };

        root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeColors colors = Palettes[(settings.AccentTheme, dark)];
        SetBrush("AppBackgroundBrush", colors.Background);
        SetBrush("AppSidebarBrush", colors.Sidebar);
        SetBrush("AppSurfaceBrush", colors.Surface);
        SetBrush("AppForegroundBrush", colors.Foreground);
        SetBrush("AppSecondaryBrush", colors.Secondary);
        SetBrush("AppAccentBrush", colors.Accent);
        SetBrush("AppBorderBrush", colors.Border);
        ApplyTitleBar(colors);
    }

    private static void ApplyTitleBar(ThemeColors colors)
    {
        if (App.MainWindow is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        AppWindowTitleBar titleBar = App.MainWindow.AppWindow.TitleBar;
        Color foreground = ToColor(colors.Foreground);
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = ToColor(colors.Secondary);
        titleBar.ButtonHoverBackgroundColor = ToColor(colors.Border);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = ToColor(colors.Accent);
        titleBar.ButtonPressedForegroundColor = ToColor(colors.Surface);
    }

    private static void SetBrush(string key, uint argb)
    {
        Color color = ToColor(argb);
        if (Application.Current.Resources[key] is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
