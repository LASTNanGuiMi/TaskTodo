using TaskTodo.Core.Interaction;

namespace TaskTodo.Core.Models;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum DockSide
{
    Left,
    Right,
}

public enum CollapseStyle
{
    Capsule,
    Orb,
}

public enum ColorPalette
{
    Indigo,
    Coral,
    Rose,
    Amber,
}

public enum SurfaceMaterial
{
    Solid,
    Glass,
    Clear,
}

public sealed record WindowPlacementSnapshot(string? MonitorId, WindowBounds Bounds);

public sealed record SettingsSnapshot(
    double BackgroundOpacity,
    bool IsAlwaysOnTop,
    ThemePreference Theme,
    WindowPlacementSnapshot? WindowPlacement,
    DockSide DockSide,
    double DockOffset,
    WindowPresentationState LastPresentationState)
{
    public Guid? SelectedListId { get; init; }

    // Absent in settings saved by earlier versions, which therefore keep the capsule rail.
    public CollapseStyle CollapseStyle { get; init; } = CollapseStyle.Capsule;

    // Also absent in earlier settings: those get the default palette on a solid surface.
    public ColorPalette Palette { get; init; } = ColorPalette.Indigo;

    // Glass blurs the desktop behind the panel, Clear shows it unblurred; both use BackgroundOpacity. Solid is opaque.
    public SurfaceMaterial Material { get; init; } = SurfaceMaterial.Solid;

    public static SettingsSnapshot Default { get; } = new(
        BackgroundOpacity: 0.88,
        IsAlwaysOnTop: true,
        Theme: ThemePreference.System,
        WindowPlacement: null,
        DockSide: DockSide.Right,
        DockOffset: 0.20,
        LastPresentationState: WindowPresentationState.Expanded);
}
