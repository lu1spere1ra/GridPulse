using MudBlazor;

namespace GridPulse.Web.Components.Layout;

/// <summary>
/// Cool "network map" palette: structure in line blue, energy in amber (the only warm colour,
/// reserved for consumption), red only for alerts.
/// </summary>
public static class GridPulseTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1F5FA8",
            Secondary = "#D9930D",
            Error = "#C0392B",
            Success = "#2E7D5B",
            Warning = "#B26A00",
            Background = "#F2F5F7",
            Surface = "#FFFFFF",
            AppbarBackground = "#1C2B36",
            AppbarText = "#E8EEF2",
            DrawerBackground = "#F2F5F7",
            TextPrimary = "#1C2B36",
            TextSecondary = "#52646F",
            LinesDefault = "#D5DEE4",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"] },
            H1 = new H1Typography { FontFamily = ["IBM Plex Sans", "sans-serif"], FontWeight = "600" },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "6px" },
    };
}
