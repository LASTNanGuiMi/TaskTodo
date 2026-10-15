using TaskTodo.Core.Models;

namespace TaskTodo.Core.Interaction;

// Every input uses the same coordinate space (DIP in the desktop adapter).
public static class EdgePlacement
{
    public static WindowBounds Dock(WindowBounds workArea, double width, double height, DockSide side, double offset)
    {
        var margin = Math.Min(8, workArea.Width / 4);
        width = Math.Min(width, workArea.Width - margin * 2);
        height = Math.Min(height, workArea.Height);
        return new WindowBounds(side == DockSide.Left ? workArea.Left + margin : workArea.Right - width - margin,
            workArea.Top + Math.Clamp(offset, 0, 1) * (workArea.Height - height), width, height);
    }

    public static WindowBounds Preview(WindowBounds dock, WindowBounds workArea, DockSide side, double height)
    {
        var width = Math.Min(320, workArea.Width);
        var desired = new WindowBounds(side == DockSide.Left ? dock.Right + 8 : dock.Left - 8 - width,
            dock.Top, width, Math.Min(420, height));
        return WorkAreaPlacement.Constrain(desired, workArea, 1, 1);
    }

    public static double Offset(WindowBounds dock, WindowBounds workArea)
        => workArea.Height <= dock.Height ? 0 : Math.Clamp((dock.Top - workArea.Top) / (workArea.Height - dock.Height), 0, 1);

    public static bool InHoverRegion(double x, double y, WindowBounds dock, WindowBounds? preview)
    {
        if (Contains(dock, x, y)) return true;
        if (preview is not { } panel) return false;
        if (Contains(panel, x, y)) return true;
        var left = Math.Min(dock.Right, panel.Right);
        var right = Math.Max(dock.Left, panel.Left);
        var top = Math.Max(dock.Top, panel.Top);
        var bottom = Math.Min(dock.Bottom, panel.Bottom);
        return right >= left && bottom >= top && x >= left && x <= right && y >= top && y <= bottom;
    }

    private static bool Contains(WindowBounds bounds, double x, double y)
        => x >= bounds.Left && x <= bounds.Right && y >= bounds.Top && y <= bounds.Bottom;
}
