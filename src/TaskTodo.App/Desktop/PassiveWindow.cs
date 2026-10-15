using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskTodo.App.Desktop;

// Hover and checkbox clicks must leave the user's current application focused.
public class PassiveWindow : Window
{
    private HwndSource? _source;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x08000000);
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(WindowMessage);
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WindowMessage);
        base.OnClosed(e);
    }

    private static IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0021) return IntPtr.Zero; // WM_MOUSEACTIVATE
        handled = true;
        return new IntPtr(3); // MA_NOACTIVATE: process the click without activating.
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr window, int index, nint value);
}
