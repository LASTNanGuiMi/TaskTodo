using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.Infrastructure.Tests.Storage;

public sealed class TaskOrderingTests
{
    [Fact]
    public async Task Reorder_UpdatesOnlyCompleteActiveSet_AndPreservesCompletedAndDeletedSortKeys()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        var first = await service.CreateTaskAsync(Guid.NewGuid(), listId, "一");
        var second = await service.CreateTaskAsync(Guid.NewGuid(), listId, "二");
        var third = await service.CreateTaskAsync(Guid.NewGuid(), listId, "三");
        var completed = await service.CreateTaskAsync(Guid.NewGuid(), listId, "已完成");
        var deleted = await service.CreateTaskAsync(Guid.NewGuid(), listId, "已删除");
        completed = await service.SetCompletedAsync(completed.Id, true);
        deleted = await service.SoftDeleteTaskAsync(deleted.Id);

        await service.ReorderActiveTasksAsync(listId, [third.Id, second.Id, first.Id]);
        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(
            [third.Id, second.Id, first.Id],
            ActiveOrder(snapshot, listId));
        Assert.Equal(
            completed.SortOrder,
            Assert.Single(snapshot.Tasks, task => task.Id == completed.Id).SortOrder);

        var restored = await service.RestoreTaskAsync(deleted.Id);
        Assert.Equal(deleted.SortOrder, restored.SortOrder);
    }

    [Fact]
    public async Task Reorder_RejectsDuplicateOmittedForeignCompletedDeletedAndMissingIdsWithoutChanges()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var inboxId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        var first = await service.CreateTaskAsync(Guid.NewGuid(), inboxId, "一");
        var second = await service.CreateTaskAsync(Guid.NewGuid(), inboxId, "二");
        var completed = await service.CreateTaskAsync(Guid.NewGuid(), inboxId, "完成");
        var deleted = await service.CreateTaskAsync(Guid.NewGuid(), inboxId, "删除");
        await service.SetCompletedAsync(completed.Id, true);
        await service.SoftDeleteTaskAsync(deleted.Id);

        var otherList = await service.CreateListAsync(Guid.NewGuid(), "其他");
        var foreign = await service.CreateTaskAsync(Guid.NewGuid(), otherList.Id, "外部");
        var expectedOrder = new[] { first.Id, second.Id };
        IReadOnlyList<Guid>[] invalidOrders =
        [
            [first.Id, first.Id],
            [first.Id],
            [first.Id, second.Id, foreign.Id],
            [first.Id, second.Id, completed.Id],
            [first.Id, second.Id, deleted.Id],
            [first.Id, second.Id, Guid.NewGuid()],
        ];

        foreach (var invalidOrder in invalidOrders)
        {
            var exception = await Assert.ThrowsAsync<TaskTodoException>(
                () => service.ReorderActiveTasksAsync(inboxId, invalidOrder));
            Assert.Equal(TaskTodoErrorCode.Validation, exception.Code);
            Assert.Equal(expectedOrder, ActiveOrder(await service.GetSnapshotAsync(), inboxId));
        }
    }

    [Fact]
    public async Task Reorder_WhenSecondUpdateFails_RollsBackFirstUpdate()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        var first = await service.CreateTaskAsync(Guid.NewGuid(), listId, "一");
        var second = await service.CreateTaskAsync(Guid.NewGuid(), listId, "二");
        var third = await service.CreateTaskAsync(Guid.NewGuid(), listId, "三");
        await service.FlushPendingWritesAsync();

        using (var connection = storage.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER InjectReorderFailure
                BEFORE UPDATE OF SortOrder ON TodoTasks
                WHEN NEW.SortOrder = 1
                BEGIN
                    SELECT RAISE(ABORT, 'injected reorder failure');
                END;
                """;
            command.ExecuteNonQuery();
        }

        var exception = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.ReorderActiveTasksAsync(listId, [third.Id, first.Id, second.Id]));

        Assert.Equal(TaskTodoErrorCode.Conflict, exception.Code);
        Assert.Equal(
            new[] { first.Id, second.Id, third.Id },
            ActiveOrder(await service.GetSnapshotAsync(), listId));
    }

    private static Guid[] ActiveOrder(TaskTodoSnapshot snapshot, Guid listId)
        => snapshot.Tasks
            .Where(task => task.ListId == listId && !task.IsCompleted && task.DeletedAtUtc is null)
            .Select(task => task.Id)
            .ToArray();
}
