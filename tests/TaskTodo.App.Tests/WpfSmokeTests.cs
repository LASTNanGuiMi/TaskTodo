using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskTodo.App.Controls;
using TaskTodo.App.Desktop;
using TaskTodo.App.State;
using TaskTodo.App.ViewModels;
using TaskTodo.App.Views;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

[Collection("Desktop")]
public sealed class WpfSmokeTests
{
    [Fact]
    public async Task Real_windows_bind_render_handle_input_and_restore_position()
    {
        var elapsed = Stopwatch.StartNew();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await VerifyWindows();
                    completion.SetResult();
                }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(90)); }
        finally { Console.WriteLine($"[WpfSmoke] Total wall time: {elapsed.Elapsed.TotalSeconds:F2}s."); }
    }

    private static async Task VerifyWindows()
    {
        var elapsed = Stopwatch.StartNew();
        void Log(string phase) => Console.WriteLine($"[WpfSmoke {elapsed.Elapsed.TotalSeconds:F2}s] {phase}");
        Log("Initializing WPF and isolated storage.");
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/TaskTodo;component/Styles/Theme.xaml", UriKind.Relative),
        });
        var bindingErrors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = new MainWindowViewModel(service, new TaskStore(await service.GetSnapshotAsync()));
        panel.IsAlwaysOnTop = false;
        var main = new MainWindow(panel) { ShowActivated = false, ShowInTaskbar = false };
        var dock = new DockRailWindow { DataContext = panel };
        try
        {
            ThemeService.Apply(ThemePreference.Light);
            main.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(main, "panel-empty-light.png");
            main.Width = 280;
            main.Height = 260;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(main, "panel-empty-minimum.png");
            main.Width = 340;
            main.Height = 440;
            foreach (var title in new[] { "整理今天的待办事项", "完成项目测试与运行说明", "中文 English <纯文本> & 符号" })
            {
                panel.NewTitle = title;
                Assert.True(await panel.AddAsync());
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            main.UpdateLayout();
            Log("Main window rendered.");
            Assert.Equal(3, Find<TaskRow>(main).Count());
            Assert.InRange(main.ActualWidth, 338, 342);
            Assert.InRange(main.ActualHeight, 438, 442);
            Assert.All(Find<TaskRow>(main), row => Assert.True(row.ActualWidth < main.ActualWidth));
            SaveImage(main, "panel-light.png");

            var picker = (ComboBox)main.FindName("ListPicker");
            var selectedListId = panel.SelectedListId;
            var pickerPeer = UIElementAutomationPeer.CreatePeerForElement(picker)!;
            var selection = Assert.IsAssignableFrom<ISelectionProvider>(pickerPeer.GetPattern(PatternInterface.Selection));
            Assert.False(selection.CanSelectMultiple);
            Assert.Single(selection.GetSelection());
            Assert.Equal(selectedListId, picker.SelectedValue);
            var expansion = Assert.IsAssignableFrom<IExpandCollapseProvider>(pickerPeer.GetPattern(PatternInterface.ExpandCollapse));
            expansion.Expand();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(picker.IsDropDownOpen);
            Assert.Equal(ExpandCollapseState.Expanded, expansion.ExpandCollapseState);
            SaveImage(main, "list-picker-open-light.png");
            // Popup content lives in a separate visual root, so render its actual surface too.
            var popup = Assert.IsType<Popup>(picker.Template.FindName("PART_Popup", picker));
            SaveImage(Assert.IsAssignableFrom<FrameworkElement>(popup.Child), "list-picker-options-light.png");
            expansion.Collapse();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(picker.IsDropDownOpen);
            Assert.Equal(selectedListId, panel.SelectedListId);
            Assert.Equal(selectedListId, picker.SelectedValue);

            panel.IsSettingsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(main, "settings-light.png");
            var settingsScroll = Find<ScrollViewer>(main).Single(scroller => scroller.IsVisible &&
                Find<Button>(scroller).Any(button => AutomationProperties.GetAutomationId(button) == "DataTools"));
            settingsScroll.ScrollToBottom();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(main, "settings-bottom.png");
            settingsScroll.ScrollToTop();
            panel.IsSettingsOpen = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var input = (TaskTextBox)main.FindName("QuickAdd");
            // The IME keeps its composition out of Text, so the hint has to hide on composition start.
            var hint = Find<TextBlock>(main).Single(block => block.Text == "添加任务，按 Enter 保存");
            Assert.Equal(Visibility.Visible, hint.Visibility);
            var pending = new TextComposition(InputManager.Current, input, "san");
            input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, pending)
            {
                RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent,
            });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Collapsed, hint.Visibility);
            input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, pending)
            {
                RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
            });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Visible, hint.Visibility);
            input.Text = "确认候选后再添加";
            var composition = new TextComposition(InputManager.Current, input, "确认");
            input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
            {
                RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent,
            });
            PressKey(input, Key.Enter);
            Assert.Equal(3, panel.PendingCount);
            input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
            {
                RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
            });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            input.Text = "确认候选后再添加";
            PressKey(input, Key.Enter);
            await WaitFor(() => panel.PendingCount == 4 && !panel.IsSaving);
            Assert.Empty(input.Text);

            var row = panel.ActiveTasks[0];
            await panel.BeginEditAsync(row);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var editor = Find<TaskTextBox>(main).Single(box => box.IsVisible && box.Name == "Editor");
            editor.Text = "不应提交的编辑";
            PressKey(editor, Key.Escape);
            Assert.False(row.IsEditing);
            Assert.Equal("整理今天的待办事项", row.Title);

            await panel.SetCompletedAsync(panel.ActiveTasks[1], true);
            ThemeService.Apply(ThemePreference.Dark);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(main, "panel-dark.png");
            panel.IsSettingsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(main, "settings-dark.png");
            panel.IsSettingsOpen = false;
            await RenderThemeGallery(main, panel);
            panel.NewTitle = new string('长', 500);
            await panel.AddAsync();
            main.Width = 280;
            main.Height = 260;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            main.UpdateLayout();
            Assert.All(Find<TaskRow>(main), control => Assert.InRange(control.ActualWidth, 1, 260));
            SaveImage(main, "panel-minimum.png");
            var taskList = Find<ListBox>(main).Single(control => AutomationProperties.GetAutomationId(control) == "TaskScroll");
            var longTask = panel.ActiveTasks.Single(task => task.Title.Length == 500);
            taskList.ScrollIntoView(longTask);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            taskList.UpdateLayout();
            var longRow = Find<TaskRow>(taskList).Single(control => ReferenceEquals(control.DataContext, longTask));
            var longBounds = longRow.TransformToAncestor(taskList).TransformBounds(new Rect(0, 0, longRow.ActualWidth, longRow.ActualHeight));
            Assert.True(longBounds.IntersectsWith(new Rect(0, 0, taskList.ActualWidth, taskList.ActualHeight)));
            SaveImage(main, "panel-minimum-long-task.png");
            Find<ScrollViewer>(taskList).First().ScrollToTop();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var completedSection = (Expander)main.FindName("CompletedSection");
            completedSection.IsExpanded = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var completedList = Find<ListBox>(completedSection).Single();
            Assert.True(taskList.ActualHeight > 0);
            var completedCheck = Find<TaskCheckBox>(completedList).First();
            Assert.True(completedList.ActualHeight >= completedCheck.ActualHeight + 12);
            var completedBounds = completedList.TransformToAncestor(main).TransformBounds(
                new Rect(0, 0, completedList.ActualWidth, completedList.ActualHeight));
            var addSurface = (FrameworkElement)main.FindName("QuickAddSurface");
            Assert.True(completedBounds.Bottom <= addSurface.TransformToAncestor(main).Transform(new System.Windows.Point()).Y);
            SaveImage(main, "panel-minimum-completed.png");
            completedSection.IsExpanded = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var monitors = new MonitorService();
            var saved = monitors.Capture(main);
            Assert.InRange(saved.Bounds.Width, 278, 282);
            main.Left = -100000;
            monitors.Restore(main, saved);
            var restored = monitors.Capture(main);
            Assert.Equal(saved.MonitorId, restored.MonitorId);
            Assert.InRange(Math.Abs(saved.Bounds.Left - restored.Bounds.Left), 0, 2);
            Assert.InRange(Math.Abs(saved.Bounds.Top - restored.Bounds.Top), 0, 2);
            monitors.PlaceDockAtRightEdge(dock, main);
            dock.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(dock, "capsule.png");
            Assert.Equal(saved.MonitorId, monitors.Capture(dock).MonitorId);
            Assert.Contains(Find<TextBlock>(dock), text => text.Text == panel.PendingCount.ToString());
            AssertPassiveWindow(dock);
            using var previewModel = new PreviewViewModel(service, panel.Store);
            var preview = new PreviewWindow(previewModel);
            monitors.PlacePreview(preview, dock, DockSide.Right, 420, dock.PointToScreen(new System.Windows.Point()).Y);
            var foreground = GetForegroundWindow();
            preview.Reveal();
            Assert.Equal(foreground, GetForegroundWindow());
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            AssertPassiveWindow(preview);
            Assert.InRange(preview.ActualWidth, 1, 320);
            Assert.InRange(preview.ActualHeight, 1, 420);
            Assert.Empty(Find<TaskTextBox>(preview));
            var previewCheck = Find<TaskCheckBox>(preview).First(check => check.IsChecked == false);
            var previewRow = (TaskRowViewModel)previewCheck.DataContext;
            var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(previewCheck)!
                .GetPattern(PatternInterface.Toggle)!;
            toggle.Toggle();
            await WaitFor(() => previewRow.IsCompleted && !previewModel.IsSaving);
            Assert.Contains(panel.CompletedTasks, task => task.Id == previewRow.Id);
            toggle.Toggle();
            await WaitFor(() => !previewRow.IsCompleted && !previewModel.IsSaving);
            Assert.Contains(panel.ActiveTasks, task => task.Id == previewRow.Id);
            await Task.Delay(200);
            SaveImage(preview, "preview-dark.png");
            preview.Close();
            var dataModel = new DataManagementViewModel(service, panel.Store);
            var data = new DataManagementWindow(dataModel)
            {
                Owner = main, ShowActivated = false, ShowInTaskbar = false,
            };
            data.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await dataModel.WaitForIdleAsync();
            SaveImage(data, "data-tools-dark.png");
            data.Close();
            var listEditor = new ListEditorWindow("新建清单", "日常安排", panel.CreateListAsync, panel)
            {
                Owner = main, ShowActivated = false, ShowInTaskbar = false,
            };
            listEditor.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(listEditor, "list-editor-dark.png");
            listEditor.Close();
            var startupRecovery = new StartupFailureWindow("无法读取已有数据，原文件已保留。请检查数据目录或从备份恢复。", storage.Options)
            {
                Owner = main, ShowActivated = false, ShowInTaskbar = false,
            };
            startupRecovery.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(startupRecovery, "startup-recovery-dark.png");
            startupRecovery.Close();
            Log("Input, placement, preview and auxiliary windows verified.");
            await VerifyVirtualizedRows(main, panel, service, storage, Log);
            Assert.Equal(1, main.Opacity);
            Assert.Empty(bindingErrors.Messages);
        }
        finally
        {
            dock.AllowApplicationExit();
            dock.Close();
            main.AllowApplicationExit();
            main.Close();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
        }
    }

    private static async Task VerifyVirtualizedRows(MainWindow main, MainWindowViewModel panel,
        TaskTodoService service, TemporaryTaskTodoStorage storage, Action<string> log)
    {
        var list = await service.CreateListAsync(Guid.NewGuid(), "一千项回收验证");
        var originalTitles = new Dictionary<Guid, string>();
        for (var index = 0; index < 1000; index++)
        {
            var task = await service.CreateTaskAsync(Guid.NewGuid(), list.Id, $"虚拟化任务 {index + 1:D4}");
            originalTitles.Add(task.Id, task.Title);
        }
        log("Prepared 1000 tasks.");
        panel.Store.ApplySnapshot(await service.GetSnapshotAsync());
        Assert.True(await panel.SelectListAsync(list.Id));
        main.Width = 340;
        main.Height = 440;
        main.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        main.UpdateLayout();
        var taskList = Find<ListBox>(main).Single(control =>
            System.Windows.Automation.AutomationProperties.GetAutomationId(control) == "TaskScroll");
        var scrolling = Find<ScrollViewer>(taskList).First();
        Assert.Equal(1000, panel.ActiveTasks.Count);
        Assert.InRange(Find<TaskRow>(taskList).Count(), 1, 49);

        var last = panel.ActiveTasks[^1];
        scrolling.ScrollToBottom();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        taskList.UpdateLayout();
        var lastControl = Find<TaskRow>(taskList).Single(control => ReferenceEquals(control.DataContext, last));
        var lastBounds = lastControl.TransformToAncestor(taskList).TransformBounds(
            new Rect(0, 0, lastControl.ActualWidth, lastControl.ActualHeight));
        Assert.True(lastBounds.IntersectsWith(new Rect(0, 0, taskList.ActualWidth, taskList.ActualHeight)));
        Assert.InRange(Find<TaskRow>(taskList).Count(), 1, 49);
        SaveImage(main, "panel-thousand-tasks.png");
        Toggle(Find<TaskCheckBox>(lastControl).Single());
        await WaitFor(() => !panel.IsSaving && panel.CompletedTasks.Any(task => task.Id == last.Id));
        var committed = (await service.GetSnapshotAsync()).Tasks.Where(task => task.ListId == list.Id).ToArray();
        Assert.Equal(last.Id, Assert.Single(committed, task => task.IsCompleted).Id);
        log("Viewport, real scrolling and last-row completion verified.");

        scrolling.ScrollToTop();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var first = panel.ActiveTasks[0];
        Assert.True(await panel.BeginEditAsync(first));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var firstControl = Find<TaskRow>(taskList).Single(control => ReferenceEquals(control.DataContext, first));
        var firstEditor = Find<TaskTextBox>(firstControl).Single();
        firstEditor.Text = "失焦只保存第一项";
        firstEditor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount,
            firstEditor, (TaskTextBox)main.FindName("QuickAdd")) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        await WaitFor(() => !panel.IsSaving && panel.EditingTask is null);
        Assert.Equal("失焦只保存第一项", first.Title);

        // Real scrolling was verified above. Rebind the waiting click explicitly here;
        // release the database lock before awaiting any dispatcher work.
        Assert.True(await panel.BeginEditAsync(first));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        firstEditor.Text = "回收后仍保存第一项";
        var target = panel.ActiveTasks[2];
        var targetControl = Find<TaskRow>(taskList).Single(control => ReferenceEquals(control.DataContext, target));
        using (var connection = storage.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Toggle(Find<TaskCheckBox>(targetControl).Single());
            Assert.True(panel.IsSaving);
            targetControl.DataContext = panel.ActiveTasks[^1];
            transaction.Commit();
        }
        log("Rebound pending click and released the write lock.");
        try
        {
            await WaitFor(() => !panel.IsSaving && panel.CompletedTasks.Any(task => task.Id == target.Id));
        }
        finally { targetControl.ClearValue(FrameworkElement.DataContextProperty); }
        committed = (await service.GetSnapshotAsync()).Tasks.Where(task => task.ListId == list.Id).ToArray();
        Assert.Equal(new[] { last.Id, target.Id }.Order(), committed.Where(task => task.IsCompleted).Select(task => task.Id).Order());
        Assert.Equal("回收后仍保存第一项", committed.Single(task => task.Id == first.Id).Title);
        Assert.All(committed.Where(task => task.Id != first.Id), task => Assert.Equal(originalTitles[task.Id], task.Title));
        Assert.InRange(Find<TaskRow>(taskList).Count(), 1, 49);
        log("Editing and rebound task identity verified.");
    }

    private static void Toggle(TaskCheckBox checkbox)
        => ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(checkbox)!
            .GetPattern(PatternInterface.Toggle)!).Toggle();

    private static void PressKey(UIElement target, Key key)
        => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });

    private static void AssertPassiveWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Assert.NotEqual(0, (long)GetWindowLongPtr(handle, -20) & 0x08000000);
        Assert.Equal(new IntPtr(3), SendMessage(handle, 0x0021, IntPtr.Zero, IntPtr.Zero));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.Fail("The WPF input action did not complete.");
    }

    private static IEnumerable<T> Find<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(element, index))) yield return child;
    }

    // Every palette, light and dark, solid and glass: binding errors are checked for all of them, and with
    // TASKTODO_TEST_ARTIFACTS set each is saved over a stand-in wallpaper (RenderTargetBitmap cannot show DWM blur).
    private static async Task RenderThemeGallery(MainWindow main, MainWindowViewModel panel)
    {
        var backdrop = new LinearGradientBrush(
            new GradientStopCollection([new GradientStop(Color.FromRgb(0x2B, 0x5C, 0xB8), 0), new GradientStop(Color.FromRgb(0x8E, 0x4F, 0xC7), 0.55),
                new GradientStop(Color.FromRgb(0xF0, 0x9A, 0x5A), 1)]), 35);
        foreach (var palette in Enum.GetValues<ColorPalette>())
        foreach (var material in Enum.GetValues<SurfaceMaterial>())
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        {
            panel.Palette = palette;
            panel.Material = material;
            ThemeService.Apply(theme, palette, material);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveImage(main, $"gallery-{palette}-{material}-{theme}.png".ToLowerInvariant(), backdrop);
        }
        panel.Palette = ColorPalette.Rose;
        panel.Material = SurfaceMaterial.Glass;
        panel.IsSettingsOpen = true;
        ThemeService.Apply(ThemePreference.Light, ColorPalette.Rose, SurfaceMaterial.Glass);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveImage(main, "gallery-settings-rose-glass-light.png", backdrop);
        panel.IsSettingsOpen = false;
        panel.Palette = ColorPalette.Indigo;
        panel.Material = SurfaceMaterial.Solid;
        panel.BackgroundOpacity = SettingsSnapshot.Default.BackgroundOpacity;
        ThemeService.Apply(ThemePreference.Dark);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void SaveImage(FrameworkElement window, string fileName, Brush? backdrop = null)
    {
        var directory = Environment.GetEnvironmentVariable("TASKTODO_TEST_ARTIFACTS");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth * 2, (int)window.ActualHeight * 2,
            192, 192, PixelFormats.Pbgra32);
        if (backdrop is null) bitmap.Render(window);
        else
        {
            var composite = new DrawingVisual();
            using (var context = composite.RenderOpen())
            {
                var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
                context.DrawRectangle(backdrop, null, bounds);
                context.DrawRectangle(new VisualBrush(window), null, bounds);
            }
            bitmap.Render(composite);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (message is not null) Messages.Add(message); }
        public override void WriteLine(string? message) { if (message is not null) Messages.Add(message); }
    }
}
