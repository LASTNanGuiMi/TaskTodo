using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace TaskTodo.App.Desktop;

// Drag tracking for the edge rail. The rail never activates, and Windows keeps mouse capture for a background window
// only while the cursor is over it, so a quick drag would outrun the 56 DIP orb and stop. Instead the cursor and the
// button state are polled once per rendered frame until the button is released.
internal sealed class PointerDrag
{
    private const int SmSwapButton = 23;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private Window? _window;
    private NativePoint _start;
    private NativePoint _last;

    public bool IsActive { get; private set; }
    public bool IsDragging { get; private set; }
    public event EventHandler? Started;
    public event EventHandler<DragDeltaEventArgs>? Moved;
    public event EventHandler<bool>? Finished;

    public void Begin(Window window)
    {
        if (IsActive || !GetCursorPos(out _start)) return;
        _last = _start;
        _window = window;
        IsActive = true;
        IsDragging = false;
        CompositionTarget.Rendering += Track;
    }

    private void Track(object? sender, EventArgs e)
    {
        var button = GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;
        if ((GetAsyncKeyState(button) & 0x8000) == 0 || !GetCursorPos(out var point))
        {
            End();
            return;
        }
        var scale = GetDpiForWindow(new WindowInteropHelper(_window!).Handle) / 96d;
        if (!IsDragging)
        {
            if (Math.Abs(point.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance * scale
                && Math.Abs(point.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance * scale) return;
            IsDragging = true;
            Started?.Invoke(this, EventArgs.Empty);
        }
        if (point.X == _last.X && point.Y == _last.Y) return;
        // DIP deltas at the window's own scale, which MoveDock turns back into the same pixel offset.
        Moved?.Invoke(this, new DragDeltaEventArgs((point.X - _last.X) / scale, (point.Y - _last.Y) / scale));
        _last = point;
    }

    private void End()
    {
        CompositionTarget.Rendering -= Track;
        var dragged = IsDragging;
        IsActive = false;
        IsDragging = false;
        Finished?.Invoke(this, dragged);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
