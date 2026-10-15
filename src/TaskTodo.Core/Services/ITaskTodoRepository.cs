using TaskTodo.Core.Models;

namespace TaskTodo.Core.Services;

public interface ITaskTodoRepository : IAsyncDisposable
{
    Task<TodoTask> CreateTaskAsync(
        Guid requestId,
        Guid listId,
        string normalizedTitle,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<TodoTask> UpdateTaskTitleAsync(
        Guid taskId,
        string normalizedTitle,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<TodoTask> SetTaskCompletedAsync(
        Guid taskId,
        bool isCompleted,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<TodoTask> SoftDeleteTaskAsync(
        Guid taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<TodoTask> RestoreTaskAsync(
        Guid taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task ReorderActiveTasksAsync(
        Guid listId,
        IReadOnlyList<Guid> orderedTaskIds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<TaskList> CreateListAsync(
        Guid requestId,
        string normalizedName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<TaskList> RenameListAsync(
        Guid listId,
        string normalizedName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<TaskTodoSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoTask>> GetDeletedTasksAsync(CancellationToken cancellationToken = default);

    Task<TaskTodoArchive> GetArchiveAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task<string> ReplaceDataAsync(TaskTodoArchive archive, DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(SettingsSnapshot settings, CancellationToken cancellationToken = default);

    Task<string> CreateBackupAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task FlushPendingWritesAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
