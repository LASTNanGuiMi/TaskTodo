using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskTodo.App.Desktop;

// Backdrop for the see-through materials of the main panel and the preview.
//
// Both are layered (AllowsTransparency) windows. DWM's blur-behind follows their per-pixel alpha, so a panel that
// fills its window gets a blurred backdrop with clean rounded corners. The accent's gradient flag must stay off:
// with it set, the transparent pixels around the panel turn black. Any failure leaves the plain translucent fill.
internal static class GlassBackdrop
{
    private const int AccentDisabled = 0;
    private const int AccentBlurBehind = 3;
    private const int WcaAccentPolicy = 19;

    // Glass blurs what is behind the window; Clear and Solid need no accent (Clear simply paints with alpha).
    public static bool Apply(Window window, Core.Models.SurfaceMaterial material)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        try
        {
            SetAccent(handle, material == Core.Models.SurfaceMaterial.Glass ? AccentBlurBehind : AccentDisabled);
            return true;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void SetAccent(IntPtr handle, int state)
    {
        var accent = new AccentPolicy { AccentState = state };
        var size = Marshal.SizeOf(accent);
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, pointer, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = pointer, SizeOfData = size };
            SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr window, ref WindowCompositionAttributeData data);
}
