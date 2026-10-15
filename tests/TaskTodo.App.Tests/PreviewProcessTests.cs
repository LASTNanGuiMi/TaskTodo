using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

[Collection("Desktop")]
public sealed class PreviewProcessTests
{
    [ExclusiveMouseFact]
    public async Task Hover_and_native_checkbox_click_keep_foreground_and_share_one_preview()
    {
        using var storage = new TemporaryTaskTodoStorage();
        Guid firstId;
        Guid secondId;
        await using (var service = await storage.OpenServiceAsync())
        {
            firstId = (await service.GetSnapshotAsync()).Lists[0].Id;
            secondId = (await service.CreateListAsync(Guid.NewGuid(), "第二预览清单")).Id;
            await service.CreateTaskAsync(Guid.NewGuid(), firstId, "不抢焦点的复选框");
            await service.CreateTaskAsync(Guid.NewGuid(), secondId, "第二清单独立任务");
            await service.SaveSettingsAsync(SettingsSnapshot.Default with { LastPresentationState = WindowPresentationState.Docked });
        }
        var originalForeground = GetForegroundWindow();
        GetCursorPos(out var originalCursor);
        using var probe = new FocusProbe();
        var focusWindow = await probe.Handle;
        Assert.NotEqual(IntPtr.Zero, focusWindow);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("TASKTODO_APP_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "TaskTodo.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(storage.RootPath);
        using var process = Process.Start(start)!;
        try
        {
            var dock = await WaitForWindow(process, "TaskTodo 胶囊");
            // Windows may reject SetForegroundWindow from a background test host.
            // A native user click establishes the unrelated foreground window first.
            SetCursorPos(200, 170);
            MouseEvent(0x0002, 0, 0, 0, UIntPtr.Zero);
            MouseEvent(0x0004, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(100);
            await WaitFor(() => GetForegroundWindow() == focusWindow);
            var first = FindById(dock, $"OpenList_{firstId}");
            MoveTo(first);
            var preview = await WaitForWindow(process, "TaskTodo 预览");
            var previewHandle = new IntPtr(preview.Current.NativeWindowHandle);
            Assert.Equal(focusWindow, GetForegroundWindow());
            Assert.NotEqual(0, (long)GetWindowLongPtr(previewHandle, -20) & 0x08000000);
            Assert.Equal(new IntPtr(3), SendMessage(previewHandle, 0x0021, IntPtr.Zero, IntPtr.Zero));
            var checkbox = preview.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));
            MoveTo(checkbox);
            MouseEvent(0x0002, 0, 0, 0, UIntPtr.Zero);
            MouseEvent(0x0004, 0, 0, 0, UIntPtr.Zero);
            await WaitFor(() => CompletedCount(storage) == 1);
            Assert.Equal(focusWindow, GetForegroundWindow());
            MoveTo(FindById(dock, $"OpenList_{secondId}"));
            await WaitFor(() => preview.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "第二清单独立任务")) is not null);
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                new PropertyCondition(AutomationElement.NameProperty, "TaskTodo 预览")));
            Assert.Equal(previewHandle.ToInt32(), Assert.Single(windows.Cast<AutomationElement>()).Current.NativeWindowHandle);
            Assert.Equal(focusWindow, GetForegroundWindow());
            SetCursorPos(110, 110);
            await WaitFor(() => FindWindow(process, "TaskTodo 预览") is null);
            Assert.Equal(focusWindow, GetForegroundWindow());
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
            probe.Dispose();
            SetCursorPos(originalCursor.X, originalCursor.Y);
            if (originalForeground != IntPtr.Zero) SetForegroundWindow(originalForeground);
            await Task.Delay(100);
        }
    }

    private static AutomationElement FindById(AutomationElement parent, string id)
        => parent.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));

    private static void MoveTo(AutomationElement element)
    {
        var rect = element.Current.BoundingRectangle;
        Assert.True(SetCursorPos((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2)));
    }

    private static AutomationElement? FindWindow(Process process, string title)
        => AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
            new PropertyCondition(AutomationElement.NameProperty, title)));

    private static async Task<AutomationElement> WaitForWindow(Process process, string title)
    {
        AutomationElement? window = null;
        await WaitFor(() => { window = FindWindow(process, title); return window is not null; });
        return window!;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail("The isolated preview process did not reach the expected state.");
    }

    private static long CompletedCount(TemporaryTaskTodoStorage storage)
    {
        using var connection = storage.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TodoTasks WHERE IsCompleted=1;";
        return (long)command.ExecuteScalar()!;
    }

    private sealed class FocusProbe : IDisposable
    {
        private readonly Thread _thread;
        private Dispatcher? _dispatcher;
        private readonly TaskCompletionSource<IntPtr> _handle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        public FocusProbe()
        {
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                var window = CreateWindowEx(0x00000008, "STATIC", "TaskTodo isolated focus probe", 0x10CF0000,
                    100, 100, 260, 110, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                _handle.SetResult(window);
                Dispatcher.Run();
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public Task<IntPtr> Handle => _handle.Task;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _dispatcher!.Invoke(() => DestroyWindow(_handle.Task.Result));
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            _thread.Join();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}

public sealed class ExclusiveMouseFactAttribute : FactAttribute
{
    public ExclusiveMouseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TASKTODO_EXCLUSIVE_MOUSE") != "1")
            Skip = "需要独占系统鼠标与前台窗口；在无人操作的交互桌面设置 TASKTODO_EXCLUSIVE_MOUSE=1 后执行。";
    }
}
