using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;

namespace TaskTodo.App.Desktop;

// Win32 rectangles and cursor positions stay in physical pixels here.
// Geometry policies and saved placements use monitor-local DIP.
internal sealed class MonitorService : IDisposable
{
    private const uint NoZOrderOrActivation = 0x0004 | 0x0010;
    private readonly List<HwndSource> _sources = [];
    public event EventHandler? EnvironmentChanged;
    public event EventHandler? BoundsSettled;

    public void Observe(Window window)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        source.AddHook(WindowMessage);
        _sources.Add(source);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x007E or 0x02E0 or 0x001A) // display, DPI, work area
            EnvironmentChanged?.Invoke(this, EventArgs.Empty);
        if (message == 0x0232) BoundsSettled?.Invoke(this, EventArgs.Empty); // end move/resize
        return IntPtr.Zero;
    }

    public bool HasMonitor(string? monitorId)
    {
        var found = false;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
        {
            if (ReadInfo(monitor).DeviceName == monitorId) found = true;
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    public WindowPlacementSnapshot Capture(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var info = ReadInfo(MonitorFromWindow(handle, 2));
        var bounds = ReadBounds(handle);
        var scale = GetDpiForWindow(handle) / 96d;
        return new WindowPlacementSnapshot(info.DeviceName,
            new WindowBounds((bounds.Left - info.WorkArea.Left) / scale,
                (bounds.Top - info.WorkArea.Top) / scale, bounds.Width / scale, bounds.Height / scale));
    }

    public void Restore(Window window, WindowPlacementSnapshot placement)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var target = MonitorFromWindow(handle, 1);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
        {
            if (ReadInfo(monitor).DeviceName == placement.MonitorId) target = monitor;
            return true;
        }, IntPtr.Zero);
        var info = ReadInfo(target);
        var scale = MonitorScale(target);
        var local = WorkAreaPlacement.Constrain(placement.Bounds, LocalWorkArea(info, scale),
            Math.Max(1, window.MinWidth), Math.Max(1, window.MinHeight));
        PlaceLocal(window, local, info, scale);
    }

    public void ConstrainToWorkArea(Window window)
    {
        if (window.WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var target = MonitorFromWindow(handle, 2);
        var info = ReadInfo(target);
        var bounds = ReadBounds(handle);
        var scale = MonitorScale(target);
        var constrained = WorkAreaPlacement.Constrain(bounds, info.WorkArea.ToBounds(),
            Math.Max(1, window.MinWidth * scale), Math.Max(1, window.MinHeight * scale));
        if (bounds != constrained) Place(handle, constrained);
    }

    // The orb window is its 44 DIP disc plus a transparent margin for the shadow and count badge.
    public const double OrbWindowSize = 56;

    public void PlaceDock(Window dockWindow, Window anchorWindow, DockSide side, double offset, int listCount,
        CollapseStyle style = CollapseStyle.Capsule)
    {
        var target = MonitorFromWindow(new WindowInteropHelper(anchorWindow).EnsureHandle(), 2);
        var info = ReadInfo(target);
        var scale = MonitorScale(target);
        var local = style == CollapseStyle.Orb
            ? EdgePlacement.Dock(LocalWorkArea(info, scale), OrbWindowSize, OrbWindowSize, side, offset)
            : EdgePlacement.Dock(LocalWorkArea(info, scale), 128, 16 + 38 * listCount, side, offset);
        PlaceLocal(dockWindow, local, info, scale);
    }

    // Kept for the initial single-capsule smoke test.
    public void PlaceDockAtRightEdge(Window dockWindow, Window anchorWindow)
        => PlaceDock(dockWindow, anchorWindow, DockSide.Right, 0.2, 1);

    public void PlacePreview(Window preview, Window dock, DockSide side, double height, double anchorTop)
    {
        var dockHandle = new WindowInteropHelper(dock).EnsureHandle();
        var monitor = MonitorFromWindow(dockHandle, 2);
        var info = ReadInfo(monitor);
        var scale = MonitorScale(monitor);
        var bounds = ReadBounds(dockHandle);
        var localDock = new WindowBounds((bounds.Left - info.WorkArea.Left) / scale,
            (anchorTop - info.WorkArea.Top) / scale, bounds.Width / scale, bounds.Height / scale);
        var local = EdgePlacement.Preview(localDock, LocalWorkArea(info, scale), side, height);
        PlaceLocal(preview, local, info, scale);
    }

    public bool CursorInHoverRegion(Window dock, Window? preview)
    {
        if (!GetCursorPos(out var point)) throw new Win32Exception();
        return EdgePlacement.InHoverRegion(point.X, point.Y,
            ReadBounds(new WindowInteropHelper(dock).Handle),
            preview is null ? null : ReadBounds(new WindowInteropHelper(preview).Handle));
    }

    public void MoveDock(Window dock, double deltaX, double deltaY)
    {
        var handle = new WindowInteropHelper(dock).Handle;
        var bounds = ReadBounds(handle);
        var scale = GetDpiForWindow(handle) / 96d;
        Place(handle, bounds with { Left = bounds.Left + deltaX * scale, Top = bounds.Top + deltaY * scale });
    }

    public (DockSide Side, double Offset) SnapDock(Window dock, Window anchor)
    {
        var dockHandle = new WindowInteropHelper(dock).Handle;
        var monitor = MonitorFromWindow(dockHandle, 2);
        var info = ReadInfo(monitor);
        var bounds = ReadBounds(dockHandle);
        var side = bounds.Left + bounds.Width / 2 < (info.WorkArea.Left + info.WorkArea.Right) / 2d
            ? DockSide.Left : DockSide.Right;
        var offset = EdgePlacement.Offset(bounds, info.WorkArea.ToBounds());
        var saved = Capture(anchor);
        if (saved.MonitorId != info.DeviceName)
            Restore(anchor, saved with { MonitorId = info.DeviceName });
        return (side, offset);
    }

    private static WindowBounds LocalWorkArea(MonitorInfo info, double scale)
        => new(0, 0, (info.WorkArea.Right - info.WorkArea.Left) / scale,
            (info.WorkArea.Bottom - info.WorkArea.Top) / scale);

    private static void PlaceLocal(Window window, WindowBounds local, MonitorInfo info, double scale)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        Place(new WindowInteropHelper(window).EnsureHandle(), new WindowBounds(
            info.WorkArea.Left + local.Left * scale, info.WorkArea.Top + local.Top * scale,
            local.Width * scale, local.Height * scale));
    }

    private static double MonitorScale(IntPtr monitor)
    {
        Marshal.ThrowExceptionForHR(GetDpiForMonitor(monitor, 0, out var x, out _));
        return x / 96d;
    }

    private static MonitorInfo ReadInfo(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), DeviceName = "" };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
        return info;
    }

    private static WindowBounds ReadBounds(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect)) throw new Win32Exception();
        return rect.ToBounds();
    }

    private static void Place(IntPtr handle, WindowBounds bounds)
    {
        if (!SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), NoZOrderOrActivation))
            throw new Win32Exception();
    }

    public void Dispose()
    {
        foreach (var source in _sources) source.RemoveHook(WindowMessage);
        _sources.Clear();
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorCallback callback, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly WindowBounds ToBounds() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea, WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}
