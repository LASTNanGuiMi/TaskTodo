using System.Globalization;
using Microsoft.Data.Sqlite;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Storage;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.Infrastructure.Tests.Storage;

public sealed class SerialQueueAndBackupTests
{
    [Fact]
    public async Task RepositoryRunsSQLiteWorkOnDedicatedThread()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var callerThreadId = Environment.CurrentManagedThreadId;
        await using var repository = await SqliteTaskTodoRepository.OpenAsync(storage.Options);

        Assert.NotEqual(0, repository.WorkerManagedThreadId);
        Assert.NotEqual(callerThreadId, repository.WorkerManagedThreadId);
    }

    [Fact]
    public async Task ConcurrentSubmissions_AreSerializedAndAllPersist()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;

        var submissions = Enumerable.Range(0, 64)
            .Select(index => service.CreateTaskAsync(Guid.NewGuid(), listId, $"任务 {index}"))
            .ToArray();
        var created = await Task.WhenAll(submissions);
        await service.FlushPendingWritesAsync();

        var snapshot = await service.GetSnapshotAsync();
        Assert.Equal(64, created.Length);
        Assert.Equal(64, snapshot.Tasks.Count);
        Assert.Equal(64, snapshot.Tasks.Select(task => task.Id).Distinct().Count());
        Assert.Equal(64, snapshot.Tasks.Select(task => task.SortOrder).Distinct().Count());
        Assert.Equal(64, snapshot.GetIncompleteCount(listId));
    }

    [Fact]
    public async Task RapidExplicitCompletionWrites_EndAtLastConfirmedValue()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        var task = await service.CreateTaskAsync(Guid.NewGuid(), listId, "快速状态");

        var setCompleted = service.SetCompletedAsync(task.Id, true);
        var setActive = service.SetCompletedAsync(task.Id, false);
        await Task.WhenAll(setCompleted, setActive);
        await service.FlushPendingWritesAsync();

        var persisted = Assert.Single((await service.GetSnapshotAsync()).Tasks);
        Assert.False(persisted.IsCompleted);
        Assert.Null(persisted.CompletedAtUtc);
        Assert.Equal(1, (await service.GetSnapshotAsync()).GetIncompleteCount(listId));
    }

    [Fact]
    public async Task Stop_DrainsAlreadyAcceptedWritesAndRejectsNewWork()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var repository = await SqliteTaskTodoRepository.OpenAsync(storage.Options);
        var service = new TaskTodoService(repository);
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;

        var pendingWrites = Enumerable.Range(0, 32)
            .Select(index => service.CreateTaskAsync(Guid.NewGuid(), listId, $"排空 {index}"))
            .ToArray();
        var stop = service.StopAsync();

        await Task.WhenAll(pendingWrites);
        await stop;
        var stoppingError = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.CreateTaskAsync(Guid.NewGuid(), listId, "停止后写入"));
        Assert.Equal(TaskTodoErrorCode.StorageStopping, stoppingError.Code);
        await service.DisposeAsync();

        await using var reopened = await storage.OpenServiceAsync();
        Assert.Equal(32, (await reopened.GetSnapshotAsync()).Tasks.Count);
    }

    [Fact]
    public async Task OnlineBackup_IsIndependentlyReadableWithCommittedWalData()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        await service.CreateTaskAsync(Guid.NewGuid(), listId, "备份一");
        await service.CreateTaskAsync(Guid.NewGuid(), listId, "备份二");

        var backupPath = await service.CreateBackupAsync();
        Assert.True(File.Exists(backupPath));

        using var backup = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        backup.Open();

        Assert.Equal(2, ExecuteScalar<long>(backup, "SELECT COUNT(*) FROM TodoTasks;"));
        Assert.Equal(1, ExecuteScalar<long>(backup, "SELECT COUNT(*) FROM TaskLists;"));
        Assert.Equal("ok", ExecuteScalar<string>(backup, "PRAGMA quick_check;"));
    }

    private static T ExecuteScalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }
}
