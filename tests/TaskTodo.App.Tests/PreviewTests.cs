using TaskTodo.App.State;
using TaskTodo.App.ViewModels;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

public sealed class PreviewTests
{
    [Fact]
    public async Task Preview_switches_lists_and_completion_updates_the_shared_panel_and_capsules()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var first = (await service.GetSnapshotAsync()).Lists[0];
        var second = await service.CreateListAsync(Guid.NewGuid(), "第二清单");
        await service.CreateTaskAsync(Guid.NewGuid(), first.Id, "主清单任务");
        await service.CreateTaskAsync(Guid.NewGuid(), second.Id, "预览任务");
        var store = new TaskStore(await service.GetSnapshotAsync());
        using var panel = new MainWindowViewModel(service, store);
        using var preview = new PreviewViewModel(service, store);
        preview.SelectList(second.Id);
        Assert.Equal("第二清单", preview.ListName);
        var row = Assert.Single(preview.Rows);
        Assert.Equal("预览任务", row.Title);
        Assert.True(await preview.SetCompletedAsync(row, true));
        Assert.Equal(0, panel.Lists.Single(list => list.Id == second.Id).PendingCount);
        Assert.Equal(1, panel.PendingCount);
        var completedAt = store.Snapshot.Tasks.Single(task => task.Id == row.Id).CompletedAtUtc;
        Assert.True(await preview.SetCompletedAsync(row, true));
        Assert.Equal(completedAt, store.Snapshot.Tasks.Single(task => task.Id == row.Id).CompletedAtUtc);
        Assert.True(await panel.SelectListAsync(second.Id));
        Assert.Single(panel.CompletedTasks);
        Assert.True(await preview.SetCompletedAsync(row, false));
        Assert.Single(panel.ActiveTasks);
        preview.SelectList(first.Id);
        Assert.Equal("主清单任务", Assert.Single(preview.Rows).Title);
    }

    [Fact]
    public async Task Pending_checkbox_does_not_publish_or_accept_duplicate_input()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var list = (await service.GetSnapshotAsync()).Lists[0];
        await service.CreateTaskAsync(Guid.NewGuid(), list.Id, "等待确认");
        using var preview = new PreviewViewModel(service, new TaskStore(await service.GetSnapshotAsync()));
        var row = Assert.Single(preview.Rows);
        using var connection = storage.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var pending = preview.SetCompletedAsync(row, true);
        Assert.True(preview.IsSaving);
        Assert.False(row.IsCompleted);
        Assert.False(await preview.SetCompletedAsync(row, true));
        transaction.Commit();
        Assert.True(await pending);
        Assert.True(row.IsCompleted);
        Assert.False(preview.IsSaving);
    }

    [Fact]
    public async Task Failed_preview_save_keeps_confirmed_checkbox_and_can_retry()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var list = (await service.GetSnapshotAsync()).Lists[0];
        await service.CreateTaskAsync(Guid.NewGuid(), list.Id, "写入失败");
        using var preview = new PreviewViewModel(service, new TaskStore(await service.GetSnapshotAsync()));
        var row = Assert.Single(preview.Rows);
        using var connection = storage.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_preview BEFORE UPDATE OF IsCompleted ON TodoTasks BEGIN SELECT RAISE(ABORT, 'test'); END;";
        command.ExecuteNonQuery();
        Assert.False(await preview.SetCompletedAsync(row, true));
        Assert.False(row.IsCompleted);
        Assert.True(preview.HasError);
        command.CommandText = "DROP TRIGGER fail_preview;";
        command.ExecuteNonQuery();
        await preview.RetryCommand.ExecuteAsync(null);
        Assert.True(row.IsCompleted);
        Assert.False(preview.HasError);
    }

    [Fact]
    public async Task Disposed_preview_unsubscribes_from_shared_snapshots()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var store = new TaskStore(await service.GetSnapshotAsync());
        var preview = new PreviewViewModel(service, store);
        preview.Dispose();
        store.ApplyTask(await service.CreateTaskAsync(Guid.NewGuid(), store.Snapshot.Lists[0].Id, "不再刷新"));
        Assert.Empty(preview.Rows);
    }

    [Fact]
    public async Task Replaced_data_clears_previous_preview_retry_and_selects_an_existing_list()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var snapshot = await service.GetSnapshotAsync();
        var task = await service.CreateTaskAsync(Guid.NewGuid(), snapshot.Lists[0].Id, "旧任务");
        var store = new TaskStore(await service.GetSnapshotAsync());
        using var preview = new PreviewViewModel(service, store);
        await service.SoftDeleteTaskAsync(task.Id);
        Assert.False(await preview.SetCompletedAsync(preview.Rows[0], true));
        Assert.True(preview.HasError);
        var replacement = await service.CreateListAsync(Guid.NewGuid(), "替换后清单");
        store.ReplaceData(snapshot with { Lists = [replacement], Tasks = [] });
        Assert.False(preview.HasError);
        Assert.Equal(replacement.Id, preview.ListId);
        Assert.Empty(preview.Rows);
        await preview.RetryCommand.ExecuteAsync(null);
        Assert.False(preview.HasError);
    }
}
