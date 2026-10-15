using TaskTodo.Core.Models;

namespace TaskTodo.Core.Services;

public sealed class TaskTodoService : IAsyncDisposable
{
    private readonly ITaskTodoRepository _repository;
    private readonly TimeProvider _timeProvider;

    public TaskTodoService(ITaskTodoRepository repository, TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<TodoTask> CreateTaskAsync(
        Guid requestId,
        Guid listId,
        string title,
        CancellationToken cancellationToken = default)
    {
        EnsureNonEmpty(requestId, nameof(requestId));
        EnsureNonEmpty(listId, nameof(listId));
        var normalizedTitle = TaskTextRules.NormalizeTitle(title);
        return _repository.CreateTaskAsync(requestId, listId, normalizedTitle, UtcNow(), cancellationToken);
    }

    public Task<TodoTask> UpdateTaskTitleAsync(
        Guid taskId,
        string title,
        CancellationToken cancellationToken = default)
    {
        EnsureNonEmpty(taskId, nameof(taskId));
        var normalizedTitle = TaskTextRules.NormalizeTitle(title);
        return _repository.UpdateTaskTitleAsync(taskId, normalizedTitle, UtcNow(), cancellationToken);
    }

    public Task<TodoTask> SetCompletedAsync(
        Guid taskId,
        bool isCompleted,
        CancellationToken cancellationToken = default)
    {
        EnsureNonEmpty(taskId, nameof(taskId));
        return _repository.SetTaskCompletedAsync(taskId, isCompleted, UtcNow(), cancellationToken);
    }

    public Task<TodoTask> SoftDeleteTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        EnsureNonEmpty(taskId, nameof(taskId));
        return _repository.SoftDeleteTaskAsync(taskId, UtcNow(), cancellationToken);
    }

    public Task<TodoTask> RestoreTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        EnsureNonEmpty(taskId, nameof(taskId));
        return _repository.RestoreTaskAsync(taskId, UtcNow(), cancellationToken);
    }

    public Task ReorderActiveTasksAsync(
        Guid listId,
        IReadOnlyList<Guid> orderedTaskIds,
        CancellationToken cancellationToken = default)
    {
        EnsureNonEmpty(listId, nameof(listId));
        ArgumentNullException.ThrowIfNull(orderedTaskIds);

        // The repository validates the complete active set inside the write transaction.
        return _repository.ReorderActiveTasksAsync(listId, orderedTaskIds.ToArray(), UtcNow(), cancellationToken);
    }

    public Task<TaskList> CreateListAsync(
        Guid requestId,
        string name,
        CancellationToken cancellationToken = default)
    {
        EnsureNonEmpty(requestId, nameof(requestId));
        var normalizedName = TaskTextRules.NormalizeListName(name);
        return _repository.CreateListAsync(requestId, normalizedName, UtcNow(), cancellationToken);
    }

    public Task<TaskList> RenameListAsync(
        Guid listId,
        string name,
        CancellationToken cancellationToken = default)
    {
        EnsureNonEmpty(listId, nameof(listId));
        var normalizedName = TaskTextRules.NormalizeListName(name);
        return _repository.RenameListAsync(listId, normalizedName, UtcNow(), cancellationToken);
    }

    public Task<TaskTodoSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => _repository.GetSnapshotAsync(cancellationToken);

    public Task<IReadOnlyList<TodoTask>> GetDeletedTasksAsync(CancellationToken cancellationToken = default)
        => _repository.GetDeletedTasksAsync(cancellationToken);

    public Task<TaskTodoArchive> GetArchiveAsync(CancellationToken cancellationToken = default)
        => _repository.GetArchiveAsync(UtcNow(), cancellationToken);

    public Task<string> ReplaceDataAsync(TaskTodoArchive archive, CancellationToken cancellationToken = default)
    {
        var validated = ArchiveRules.ValidateAndCopy(archive);
        return _repository.ReplaceDataAsync(validated, UtcNow(), cancellationToken);
    }

    public Task SaveSettingsAsync(
        SettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        SettingsRules.Validate(settings);
        return _repository.SaveSettingsAsync(settings, cancellationToken);
    }

    public Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
        => _repository.CreateBackupAsync(UtcNow(), cancellationToken);

    public Task FlushPendingWritesAsync(CancellationToken cancellationToken = default)
        => _repository.FlushPendingWritesAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _repository.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _repository.DisposeAsync();

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow().ToUniversalTime();

    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new TaskTodoException(
                TaskTodoErrorCode.Validation,
                $"{parameterName} cannot be an empty GUID.");
        }
    }

}
