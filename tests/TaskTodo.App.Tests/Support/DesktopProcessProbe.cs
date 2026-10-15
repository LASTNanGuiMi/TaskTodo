using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace TaskTodo.App.Tests.Support;

internal static class DesktopProcessProbe
{
    public static Process Start(string dataDirectory)
    {
        var executable = Environment.GetEnvironmentVariable("TASKTODO_APP_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "TaskTodo.exe");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(dataDirectory);
        return Process.Start(start)!;
    }

    public static async Task<AutomationElement> WaitForWindow(Process process, string title)
    {
        AutomationElement? found = null;
        await WaitFor(() =>
        {
            Assert.False(process.HasExited, $"TaskTodo exited before showing '{title}'.");
            var handle = FindVisibleWindow(process.Id, title);
            if (handle == IntPtr.Zero) return false;
            var className = new StringBuilder(256);
            GetClassName(handle, className, className.Capacity);
            if (className.ToString().StartsWith("HwndWrapper", StringComparison.Ordinal))
            {
                // A Win32 titlebar proxy may precede WPF's provider. Reacquire the actual
                // provider each time; never search through the task-list descendants.
                found = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                    new PropertyCondition(AutomationElement.NameProperty, title),
                    new PropertyCondition(AutomationElement.FrameworkIdProperty, "WPF")))
                    ?? AutomationElement.FromHandle(handle); // Owned WPF dialogs can be outside Root.Children.
                return found.Current.FrameworkId == "WPF"
                    && TreeWalker.RawViewWalker.GetFirstChild(found) is not null;
            }
            found = AutomationElement.FromHandle(handle);
            return TreeWalker.RawViewWalker.GetFirstChild(found) is not null;
        });
        return found!;
    }

    public static AutomationElement ById(AutomationElement parent, string automationId)
        => parent.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    public static Task<AutomationElement> WaitForEnabledById(AutomationElement parent, string automationId)
        => WaitForEnabledControl(parent,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId), automationId);

    public static async Task<AutomationElement> WaitForEnabledControl(
        AutomationElement parent, Condition condition, string description)
    {
        AutomationElement? control = null;
        await WaitFor(() =>
        {
            control = parent.FindFirst(TreeScope.Descendants, condition);
            return control is not null && control.Current.IsEnabled && !control.Current.IsOffscreen;
        }, $"Control '{description}' did not become visible and enabled within fifteen seconds.");
        return control!;
    }

    public static void Invoke(AutomationElement button)
        => ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    public static async Task WaitFor(Func<bool> condition,
        string failureMessage = "The application did not reach the expected desktop state within fifteen seconds.")
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail(failureMessage);
    }

    public static int CountWindows(int processId)
    {
        var count = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == processId) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }

    private static IntPtr FindVisibleWindow(int processId, string title)
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != processId || !IsWindowVisible(window)) return true;
            var text = new StringBuilder(256);
            GetWindowText(window, text, text.Capacity);
            if (text.ToString() != title) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static async Task Stop(Process process)
    {
        // Every process passed here was launched by the test with an isolated temporary directory.
        if (!process.HasExited) process.Kill();
        await process.WaitForExitAsync();
        await Task.Delay(100);
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumLength);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumLength);
}
