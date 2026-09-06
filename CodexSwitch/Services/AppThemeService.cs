using Avalonia.Media;
using Avalonia.Styling;
using CodexSwitchUI.Themes;
using CodexSwitchUI.Tokens;

namespace CodexSwitch.Services;

public static class AppThemeService
{
    private static string _theme = "system";
    private static bool _isListeningForSystemTheme;
    private static readonly CodexSwitchPalette LightPalette = new(
        Background: "#FFF6F8FC",
        Foreground: "#FF10213E",
        Card: "#FFFFFFFF",
        CardForeground: "#FF10213E",
        Popover: "#FFFFFFFF",
        PopoverForeground: "#FF10213E",
        Primary: "#FF2F6BFF",
        PrimaryForeground: "#FFFFFFFF",
        Secondary: "#FFEEF3FB",
        SecondaryForeground: "#FF18325C",
        Muted: "#FFF2F5FA",
        MutedForeground: "#FF60708B",
        Accent: "#FFEAF1FF",
        AccentForeground: "#FF1E4FC4",
        Destructive: "#FFE5486D",
        DestructiveForeground: "#FFFFFFFF",
        Border: "#FFDDE6F3",
        Input: "#FFCEDAEC",
        Ring: "#FF2F6BFF",
        Success: "#FF16A36A",
        SuccessForeground: "#FFFFFFFF",
        Warning: "#FFD98A15",
        WarningForeground: "#FFFFFFFF")
    {
        ProviderCardActive = "#FFF0F5FF",
        ProviderCardActiveHover = "#FFE8F0FF",
        ProviderCardActivePressed = "#FFDCE8FF",
        ProviderCardActiveBorder = "#FFAFC7FF",
        ProviderCardActiveForeground = "#FF10213E",
        ProviderCardActiveMutedForeground = "#FF526685",
        ProviderCardActiveIcon = "#FFE6EEFF",
        ProviderUsage = "#FFFFFFFF",
        ProviderUsageHover = "#FFF7FAFF",
        ProviderUsageBorder = "#FFC7D8F6"
    };

    private static readonly CodexSwitchPalette DarkPalette = new(
        Background: "#FF121418",
        Foreground: "#FFEDF0F5",
        Card: "#FF1A1D23",
        CardForeground: "#FFEDF0F5",
        Popover: "#FF1A1D23",
        PopoverForeground: "#FFEDF0F5",
        Primary: "#FF5B8CFF",
        PrimaryForeground: "#FFFFFFFF",
        Secondary: "#FF242830",
        SecondaryForeground: "#FFEDF0F5",
        Muted: "#FF20242B",
        MutedForeground: "#FF9BA3B0",
        Accent: "#FF223454",
        AccentForeground: "#FFD9E5FF",
        Destructive: "#FFFF6482",
        DestructiveForeground: "#FFFFFFFF",
        Border: "#FF323842",
        Input: "#FF3A414D",
        Ring: "#FF5B8CFF",
        Success: "#FF36C98B",
        SuccessForeground: "#FF052E20",
        Warning: "#FFF2B84B",
        WarningForeground: "#FF352300")
    {
        ProviderCardActive = "#FF182946",
        ProviderCardActiveHover = "#FF1C3154",
        ProviderCardActivePressed = "#FF203960",
        ProviderCardActiveBorder = "#FF4369A8",
        ProviderCardActiveForeground = "#FFF4F7FC",
        ProviderCardActiveMutedForeground = "#FFB2C1D7",
        ProviderCardActiveIcon = "#FF223A60",
        ProviderUsage = "#FF101A2D",
        ProviderUsageHover = "#FF142238",
        ProviderUsageBorder = "#FF334A70"
    };

    private static readonly CodexSwitchThemeOptions ComponentThemeOptions = CodexSwitchThemeOptions.ShadcnDefault with
    {
        LightPalette = LightPalette,
        DarkPalette = DarkPalette,
        Density = CodexSwitchDensity.Compact,
        Radius = 8,
        FontFamily = AppFonts.DefaultFontFamily
    };

    private static readonly IReadOnlyDictionary<string, ThemeColorPair> ThemeBrushes =
        new Dictionary<string, ThemeColorPair>(StringComparer.Ordinal)
        {
            ["CsBackgroundBrush"] = new("#121418", "#F6F8FC"),
            ["CsForegroundBrush"] = new("#EDF0F5", "#10213E"),
            ["CsCardBrush"] = new("#1A1D23", "#FFFFFF"),
            ["CsCardForegroundBrush"] = new("#EDF0F5", "#10213E"),
            ["CsPopoverBrush"] = new("#1A1D23", "#FFFFFF"),
            ["CsPopoverForegroundBrush"] = new("#EDF0F5", "#10213E"),
            ["CsPrimaryBrush"] = new("#5B8CFF", "#2F6BFF"),
            ["CsPrimaryForegroundBrush"] = new("#FFFFFF", "#FFFFFF"),
            ["CsSecondaryBrush"] = new("#242830", "#EEF3FB"),
            ["CsSecondaryForegroundBrush"] = new("#EDF0F5", "#18325C"),
            ["CsMutedBrush"] = new("#20242B", "#F2F5FA"),
            ["CsMutedForegroundBrush"] = new("#9BA3B0", "#60708B"),
            ["CsAccentBrush"] = new("#223454", "#EAF1FF"),
            ["CsAccentForegroundBrush"] = new("#D9E5FF", "#1E4FC4"),
            ["CsDestructiveBrush"] = new("#FF6482", "#E5486D"),
            ["CsDestructiveForegroundBrush"] = new("#FFFFFF", "#FFFFFF"),
            ["CsBorderBrush"] = new("#323842", "#DDE6F3"),
            ["CsAlertBorderBrush"] = new("#FF6482", "#E5486D"),
            ["CsInputBrush"] = new("#3A414D", "#CEDAEC"),
            ["CsRingBrush"] = new("#5B8CFF", "#2F6BFF"),
            ["CsSidebarBrush"] = new("#16191F", "#FFFFFF"),
            ["CsSidebarForegroundBrush"] = new("#EDF0F5", "#10213E"),
            ["CsSidebarPrimaryBrush"] = new("#5B8CFF", "#2F6BFF"),
            ["CsSidebarPrimaryForegroundBrush"] = new("#FFFFFF", "#FFFFFF"),
            ["CsSidebarAccentBrush"] = new("#223454", "#EAF1FF"),
            ["CsSidebarAccentForegroundBrush"] = new("#D9E5FF", "#1E4FC4"),
            ["CsSidebarBorderBrush"] = new("#323842", "#DDE6F3"),
            ["CsSidebarRingBrush"] = new("#5B8CFF", "#2F6BFF"),
            ["CsSidebarSurfaceBrush"] = new("#16191F", "#FFFFFF"),
            ["CsNavRestForegroundBrush"] = new("#A0A7B2", "#526681"),
            ["CsNavHoverBrush"] = new("#20252D", "#F1F5FC"),
            ["CsNavActiveBrush"] = new("#1D3153", "#EAF1FF"),
            ["CsNavActiveBorderBrush"] = new("#3D609B", "#C9D9F8"),
            ["CsNavAccentBrush"] = new("#5B8CFF", "#2F6BFF"),
            ["CsNavActiveForegroundBrush"] = new("#FFFFFF", "#1749BD"),
            ["CsNavActiveIconBrush"] = new("#8EAEFF", "#2F6BFF"),
            ["CsGroupLabelBrush"] = new("#777F8C", "#7A8AA3"),
            ["CsProviderIconShellBrush"] = new("#20242B", "#F4F7FC"),
            ["CsProviderIconShellBorderBrush"] = new("#353C47", "#DCE5F3"),
            ["CsSuccessBrush"] = new("#36C98B", "#16A36A"),
            ["CsWarningBrush"] = new("#F2B84B", "#D98A15"),
            ["CsHoverBorderBrush"] = new("#3B4C68", "#C8D6E9"),
            ["CsSubtleButtonBrush"] = new("#20242B", "#FFFFFF"),
            ["CsSubtleButtonHoverBrush"] = new("#223454", "#EAF1FF"),
            ["CsPrimaryHoverBrush"] = new("#73A0FF", "#245DEB"),
            ["CsPrimaryPressedBrush"] = new("#4B7EF1", "#1F52D2"),
            ["CsSecondaryHoverBrush"] = new("#2C313A", "#E4EBF6"),
            ["CsSecondaryPressedBrush"] = new("#353B46", "#D7E1F0"),
            ["CsOutlineButtonBrush"] = new("#1A1D23", "#FFFFFF"),
            ["CsOutlineButtonHoverBrush"] = new("#223454", "#F1F5FC"),
            ["CsOutlineButtonPressedBrush"] = new("#2A3A55", "#E8EEF8"),
            ["CsButtonAccentHoverBrush"] = new("#223454", "#EAF1FF"),
            ["CsButtonAccentPressedBrush"] = new("#2A3D60", "#DCE8FF"),
            ["CsDestructiveHoverBrush"] = new("#F17891", "#D83C60"),
            ["CsDestructivePressedBrush"] = new("#E65372", "#C92F53"),
            ["CsPressedBrush"] = new("#303640", "#DFE8F5"),
            ["CsActiveButtonBrush"] = new("#121418", "#FFFFFF"),
            ["CsSegmentedBrush"] = new("#20242B", "#EDF2F9"),
            ["CsSegmentedPillBrush"] = new("#263854", "#FFFFFF"),
            ["CsSegmentedPillBorderBrush"] = new("#3B4C68", "#D4DFEE"),
            ["CsAppSwitcherPillBrush"] = new("#20385E", "#E6EEFF"),
            ["CsInputBackgroundBrush"] = new("#1A1D23", "#FFFFFF"),
            ["CsInputFocusBackgroundBrush"] = new("#1D2128", "#FFFFFF"),
            ["CsProviderCardHoverBrush"] = new("#20242B", "#F8FAFE"),
            ["CsProviderCardActiveBrush"] = new("#182946", "#F0F5FF"),
            ["CsProviderCardActiveHoverBrush"] = new("#1C3154", "#E8F0FF"),
            ["CsProviderCardActivePressedBrush"] = new("#203960", "#DCE8FF"),
            ["CsProviderUsageBrush"] = new("#101A2D", "#FFFFFF"),
            ["CsProviderUsageHoverBrush"] = new("#142238", "#F7FAFF"),
            ["CsProviderUsageBorderBrush"] = new("#334A70", "#C7D8F6"),
            ["CsDialogCardBrush"] = new("#1A1D23", "#FFFFFF"),
            ["CsDialogSectionBrush"] = new("#171A20", "#F8FAFD"),
            ["CsDialogSectionHoverBrush"] = new("#20242B", "#F1F5FC"),
            ["CsRouteRowBrush"] = new("#171A20", "#F8FAFD"),
            ["CsRouteRowHoverBrush"] = new("#20242B", "#EFF4FB"),
            ["CsModelCardBrush"] = new("#1A1D23", "#FFFFFF"),
            ["CsModelCardHoverBrush"] = new("#20242B", "#F8FAFD"),
            ["CsPriceChipBrush"] = new("#20242B", "#F4F7FC"),
            ["CsPriceChipHoverBrush"] = new("#223454", "#FFFFFF"),
            ["CsIconButtonDangerBrush"] = new("#342033", "#FFF1F4"),
            ["CsIconButtonDangerBorderBrush"] = new("#794058", "#F5B3C1"),
            ["CsIconButtonDangerHoverBrush"] = new("#462239", "#FFE4EA"),
            ["CsIconButtonDangerHoverBorderBrush"] = new("#C55270", "#E5486D")
        };

    public static string Normalize(string? theme)
    {
        return theme?.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };
    }

    public static void Apply(string? theme)
    {
        var app = Application.Current;
        if (app is null)
            return;

        _theme = Normalize(theme);
        app.RequestedThemeVariant = _theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        EnsureSystemThemeListener(app);
        ApplyComponentLibraryTheme(app);
        ApplyBrushes(app);
    }

    private static void EnsureSystemThemeListener(Application app)
    {
        if (_isListeningForSystemTheme)
            return;

        app.ActualThemeVariantChanged += (_, _) =>
        {
            if (_theme == "system")
            {
                ApplyComponentLibraryTheme(app);
                ApplyBrushes(app);
            }
        };
        _isListeningForSystemTheme = true;
    }

    private static void ApplyComponentLibraryTheme(Application app)
    {
        var mode = _theme switch
        {
            "light" => CodexSwitchThemeMode.Light,
            "dark" => CodexSwitchThemeMode.Dark,
            _ => CodexSwitchThemeMode.System
        };

        CodexSwitchThemeManager.Current.Apply(app, mode, ComponentThemeOptions);
    }

    private static void ApplyBrushes(Application app)
    {
        var light = _theme == "light" ||
            (_theme == "system" && app.ActualThemeVariant == ThemeVariant.Light);

        foreach (var pair in ThemeBrushes)
            ApplyBrush(app, pair.Key, light ? pair.Value.Light : pair.Value.Dark);
    }

    private static void ApplyBrush(Application app, string key, string colorText)
    {
        var color = Color.Parse(colorText);
        if (app.TryGetResource(key, app.ActualThemeVariant, out var resource) &&
            resource is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        app.Resources[key] = new SolidColorBrush(color);
    }

    private readonly record struct ThemeColorPair(string Dark, string Light);
}
