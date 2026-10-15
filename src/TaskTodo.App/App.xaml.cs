using System.IO;
using System.Windows;
using System.Windows.Threading;
using TaskTodo.App.Desktop;
using TaskTodo.App.State;
using TaskTodo.App.ViewModels;
using TaskTodo.App.Views;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.App;

public partial class App : System.Windows.Application
{
    private WindowManager? _windowManager;
    private TaskTodoService? _service;
    private SingleInstanceService? _instance;
    private DataManagementViewModel? _dataTools;
    private bool _activationPending;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length != 0 && (e.Args.Length != 2 || e.Args[0] != "--data-dir" || !Path.IsPathFullyQualified(e.Args[1])))
        {
            System.Windows.MessageBox.Show("用法：TaskTodo.exe [--data-dir <绝对路径>]", "TaskTodo");
            Shutdown(1);
            return;
        }
        var options = e.Args.Length == 0 ? TaskTodoStorageOptions.CreateDefault()
            : new TaskTodoStorageOptions(Path.Combine(e.Args[1], "tasktodo.db"), Path.Combine(e.Args[1], "Backups"));

        try
        {
            _instance = await SingleInstanceService.StartAsync(Path.GetDirectoryName(options.DatabasePath)!,
                () => Dispatcher.BeginInvoke(() =>
                {
                    if (_windowManager is null) _activationPending = true;
                    else _windowManager.ShowOrRecover();
                }));
            if (_instance is null) { Shutdown(); return; }
            ObserveInstanceListenerAsync(_instance);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException)
        {
            System.Windows.MessageBox.Show("无法联系已有实例或建立实例通知：" + error.Message, "TaskTodo");
            Shutdown(1);
            return;
        }

        while (true)
        {
            try
            {
                var repository = await SqliteTaskTodoRepository.OpenAsync(options);
                _service = new TaskTodoService(repository);
                var store = new TaskStore(await _service.GetSnapshotAsync());
                var viewModel = new MainWindowViewModel(_service, store);
                _dataTools = new DataManagementViewModel(_service, store);
                _windowManager = new WindowManager(_service, viewModel);
                _windowManager.DataToolsRequested += (owner, _) =>
                {
                    new DataManagementWindow(_dataTools) { Owner = (Window)owner! }.ShowDialog();
                    viewModel.ReloadSettings();
                };
                _windowManager.Initialize();
                if (_activationPending) _windowManager.ShowOrRecover();
                return;
            }
            catch (TaskTodoException error)
            {
                if (_service is not null) await _service.DisposeAsync();
                _service = null;
                var dialog = new StartupFailureWindow(OperationError.Describe(error), options);
                if (dialog.ShowDialog() != true)
                {
                    Shutdown(1);
                    return;
                }
            }
        }
    }

    private async void ObserveInstanceListenerAsync(SingleInstanceService instance)
    {
        try { await instance.Completion; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show("实例通知已停止：" + error.Message + "\n请保存后重新启动 TaskTodo。", "TaskTodo");
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (_windowManager is null) return;
        var shutdown = PrepareSessionExitAsync();
        if (!shutdown.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = shutdown.ContinueWith(_ => Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        e.Cancel = !shutdown.GetAwaiter().GetResult();
    }

    private async Task<bool> PrepareSessionExitAsync()
    {
        if (_dataTools is not null) await _dataTools.WaitForIdleAsync();
        return await _windowManager!.PrepareShutdownAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _windowManager?.Dispose();
        _service?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _instance?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}

