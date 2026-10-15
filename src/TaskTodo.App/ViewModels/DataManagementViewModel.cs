using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskTodo.App.State;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Backup;

namespace TaskTodo.App.ViewModels;

public sealed class DataManagementViewModel(TaskTodoService service, TaskStore store) : ObservableObject
{
    private readonly JsonDataTransferService _transfer = new(service);
    private bool _isBusy;
    private string? _message;
    private Task _pending = Task.CompletedTask;

    public ObservableCollection<DeletedTaskEntry> DeletedTasks { get; } = [];
    public bool IsBusy
    {
        get => _isBusy;
        private set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(CanInteract)); }
    }
    public bool CanInteract => !IsBusy;
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public Task WaitForIdleAsync() => _pending;

    public Task RefreshAsync() => RunAsync(LoadDeletedAsync);

    public Task BackupAsync() => RunAsync(async () =>
        Message = "备份已保存：" + await service.CreateBackupAsync());

    public Task ExportAsync(string path) => RunAsync(async () =>
    {
        await _transfer.ExportAsync(path);
        Message = "导出完成：" + path;
    });

    public Task RestoreTaskAsync(DeletedTaskEntry entry) => RunAsync(async () =>
    {
        store.ApplyTask(await service.RestoreTaskAsync(entry.Id));
        DeletedTasks.Remove(entry);
        Message = "任务已恢复到原清单。";
    });

    public Task ImportAsync(string path, bool sqliteBackup, Func<TaskTodoImportPreview, bool> confirm)
        => RunAsync(async () =>
        {
            var preview = sqliteBackup
                ? await SqliteRecoveryService.ReadBackupAsync(path)
                : await _transfer.PreviewImportAsync(path);
            if (!confirm(preview)) return;
            var backup = await _transfer.ImportAsync(preview);
            store.ReplaceData(await service.GetSnapshotAsync());
            await LoadDeletedAsync();
            Message = "数据已恢复，替换前的备份保存在：" + backup;
        });

    private async Task LoadDeletedAsync()
    {
        var tasks = await service.GetDeletedTasksAsync();
        var names = store.Snapshot.Lists.ToDictionary(list => list.Id, list => list.Name);
        DeletedTasks.Clear();
        foreach (var task in tasks)
            DeletedTasks.Add(new DeletedTaskEntry(task.Id, task.Title, names[task.ListId], task.DeletedAtUtc!.Value.ToLocalTime()));
    }

    private Task RunAsync(Func<Task> operation)
    {
        if (IsBusy) return _pending;
        return _pending = ExecuteAsync(operation);
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        IsBusy = true;
        Message = null;
        try { await operation(); }
        catch (TaskTodoException error)
        {
            Message = error.Code == TaskTodoErrorCode.Validation
                ? "文件内容或格式无效。请选择 TaskTodo 导出的 JSON（不超过 32 MiB）或有效的数据库备份。"
                : OperationError.Describe(error);
        }
        finally { IsBusy = false; }
    }
}

public sealed record DeletedTaskEntry(Guid Id, string Title, string ListName, DateTimeOffset DeletedAt);
