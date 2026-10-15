namespace TaskTodo.Core.Interaction;

/// <summary>
/// Pure geometry rules. Inputs must already use the same coordinate space.
/// Pixel/DIP conversion stays in the desktop adapter.
/// </summary>
public static class WorkAreaPlacement
{
    public static WindowBounds Constrain(
        WindowBounds desired,
        WindowBounds workArea,
        double minimumWidth,
        double minimumHeight)
    {
        Validate(desired, nameof(desired));
        Validate(workArea, nameof(workArea));

        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "Work area must have a positive size.");
        }

        if (!double.IsFinite(minimumWidth) || minimumWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        }

        if (!double.IsFinite(minimumHeight) || minimumHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumHeight));
        }

        var effectiveMinimumWidth = Math.Min(minimumWidth, workArea.Width);
        var effectiveMinimumHeight = Math.Min(minimumHeight, workArea.Height);
        var width = Math.Clamp(desired.Width, effectiveMinimumWidth, workArea.Width);
        var height = Math.Clamp(desired.Height, effectiveMinimumHeight, workArea.Height);
        var left = Math.Clamp(desired.Left, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(desired.Top, workArea.Top, workArea.Bottom - height);

        return new WindowBounds(left, top, width, height);
    }

    private static void Validate(WindowBounds bounds, string parameterName)
    {
        if (!bounds.IsFinite || bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Bounds must be finite and have a positive size.");
        }
    }
}
