using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.Infrastructure.Tests.Storage;

public sealed class TaskStateTests
{
    [Fact]
    public async Task SetCompleted_IsExplicitAndIdempotent_AndCancelClearsTimestamp()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 20, 4, 0, 0, TimeSpan.Zero));
        await using var service = await storage.OpenServiceAsync(clock);
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        var task = await service.CreateTaskAsync(Guid.NewGuid(), listId, "状态测试");

        clock.Advance(TimeSpan.FromMinutes(1));
        var completed = await service.SetCompletedAsync(task.Id, true);
        Assert.True(completed.IsCompleted);
        Assert.Equal(clock.GetUtcNow(), completed.CompletedAtUtc);
        var firstCompletedAt = completed.CompletedAtUtc;
        var firstUpdatedAt = completed.UpdatedAtUtc;

        clock.Advance(TimeSpan.FromMinutes(5));
        var repeated = await service.SetCompletedAsync(task.Id, true);
        Assert.Equal(firstCompletedAt, repeated.CompletedAtUtc);
        Assert.Equal(firstUpdatedAt, repeated.UpdatedAtUtc);

        clock.Advance(TimeSpan.FromMinutes(1));
        var cancelled = await service.SetCompletedAsync(task.Id, false);
        Assert.False(cancelled.IsCompleted);
        Assert.Null(cancelled.CompletedAtUtc);
        Assert.Equal(clock.GetUtcNow(), cancelled.UpdatedAtUtc);

        clock.Advance(TimeSpan.FromMinutes(1));
        var repeatedCancel = await service.SetCompletedAsync(task.Id, false);
        Assert.Equal(cancelled.UpdatedAtUtc, repeatedCancel.UpdatedAtUtc);
        Assert.Null(repeatedCancel.CompletedAtUtc);
    }

    [Fact]
    public async Task SoftDeleteAndRestore_PreserveIdentityContentCompletionAndSortOrder()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 20, 5, 0, 0, TimeSpan.Zero));
        await using var service = await storage.OpenServiceAsync(clock);
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        var created = await service.CreateTaskAsync(Guid.NewGuid(), listId, "需要恢复");
        clock.Advance(TimeSpan.FromMinutes(1));
        var completed = await service.SetCompletedAsync(created.Id, true);

        clock.Advance(TimeSpan.FromMinutes(1));
        var deleted = await service.SoftDeleteTaskAsync(created.Id);
        Assert.NotNull(deleted.DeletedAtUtc);
        var afterDelete = await service.GetSnapshotAsync();
        Assert.DoesNotContain(afterDelete.Tasks, task => task.Id == created.Id);
        Assert.Equal(0, afterDelete.GetIncompleteCount(listId));

        clock.Advance(TimeSpan.FromMinutes(1));
        var restored = await service.RestoreTaskAsync(created.Id);
        Assert.Equal(completed.Id, restored.Id);
        Assert.Equal(completed.ListId, restored.ListId);
        Assert.Equal(completed.Title, restored.Title);
        Assert.Equal(completed.IsCompleted, restored.IsCompleted);
        Assert.Equal(completed.CompletedAtUtc, restored.CompletedAtUtc);
        Assert.Equal(completed.SortOrder, restored.SortOrder);
        Assert.Null(restored.DeletedAtUtc);

        var afterRestore = await service.GetSnapshotAsync();
        Assert.Contains(afterRestore.Tasks, task => task.Id == created.Id);
        Assert.Equal(0, afterRestore.GetIncompleteCount(listId));
    }

    [Fact]
    public async Task UpdateTitle_IsNormalizedAndIdempotent()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 20, 6, 0, 0, TimeSpan.Zero));
        await using var service = await storage.OpenServiceAsync(clock);
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        var created = await service.CreateTaskAsync(Guid.NewGuid(), listId, "旧标题");

        clock.Advance(TimeSpan.FromMinutes(1));
        var updated = await service.UpdateTaskTitleAsync(created.Id, "  新\n标题  ");
        Assert.Equal("新 标题", updated.Title);

        clock.Advance(TimeSpan.FromMinutes(1));
        var repeated = await service.UpdateTaskTitleAsync(created.Id, "新 标题");
        Assert.Equal(updated.UpdatedAtUtc, repeated.UpdatedAtUtc);

        var blank = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.UpdateTaskTitleAsync(created.Id, "  \r\n "));
        Assert.Equal(TaskTodoErrorCode.Validation, blank.Code);
        Assert.Equal("新 标题", Assert.Single((await service.GetSnapshotAsync()).Tasks).Title);
    }
}
