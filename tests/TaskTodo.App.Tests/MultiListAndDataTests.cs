using TaskTodo.App.State;
using TaskTodo.App.ViewModels;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Backup;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

public sealed class MultiListAndDataTests
{
    [Fact]
    public async Task CreateAndRenameList_PublishCommittedListsAndPersistSelectedList()
    {
        using var storage = new TemporaryTaskTodoStorage();
        Guid selected;
        await using (var service = await storage.OpenServiceAsync())
        {
            using var panel = await OpenPanel(service);
            using var other = new MainWindowViewModel(service, panel.Store);
            Assert.True(await panel.CreateListAsync("  工作\n清单  "));
            selected = panel.SelectedListId;
            Assert.Equal("工作 清单", panel.ListName);
            Assert.Equal(2, other.Lists.Count);
            Assert.True(await panel.RenameListAsync("项目任务"));
            Assert.Equal("项目任务", other.Lists.Single(list => list.Id == selected).Name);
            Assert.True(await panel.SaveSettingsAsync());
        }

        await using var reopened = await storage.OpenServiceAsync();
        using var restored = await OpenPanel(reopened);
        Assert.Equal(selected, restored.SelectedListId);
        Assert.Equal(selected, restored.Settings.SelectedListId);
        Assert.Equal("项目任务", restored.ListName);
        Assert.Equal(2, restored.Lists.Count);
    }

    [Fact]
    public async Task SwitchingList_CommitsEditorAndQuickAddToTheirOriginalList()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var target = await service.CreateListAsync(Guid.NewGuid(), "目标清单");
        using var panel = await OpenPanel(service);
        var originalList = panel.SelectedListId;
        var edited = await Add(panel, "旧标题");
        Assert.True(await panel.BeginEditAsync(edited));
        edited.EditTitle = "切换前编辑";
        panel.NewTitle = "切换前新增";

        Assert.True(await panel.SelectListAsync(target.Id));
        Assert.Equal(target.Id, panel.SelectedListId);
        Assert.Null(panel.EditingTask);
        Assert.Empty(panel.NewTitle);
        Assert.Empty(panel.ActiveTasks);
        var tasks = (await service.GetSnapshotAsync()).Tasks;
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, task => Assert.Equal(originalList, task.ListId));
        Assert.Equal("切换前编辑", tasks.Single(task => task.Id == edited.Id).Title);
        Assert.Contains(tasks, task => task.Title == "切换前新增");
        Assert.True(await panel.SelectListAsync(originalList));
        Assert.Equal(2, panel.PendingCount);
    }

    [Fact]
    public async Task InvalidDraft_BlocksListSwitchAndPreservesInput()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var target = await service.CreateListAsync(Guid.NewGuid(), "目标清单");
        using var panel = await OpenPanel(service);
        var originalList = panel.SelectedListId;
        panel.NewTitle = new string('中', 501);

        Assert.False(await panel.SelectListAsync(target.Id));
        Assert.Equal(originalList, panel.SelectedListId);
        Assert.Equal(501, panel.NewTitle.Length);
        Assert.True(panel.HasError);
        Assert.Empty((await service.GetSnapshotAsync()).Tasks);
        panel.NewTitle = "修正后的草稿";
        Assert.True(await panel.SelectListAsync(target.Id));
        Assert.Equal(originalList, Assert.Single((await service.GetSnapshotAsync()).Tasks).ListId);
    }

    [Fact]
    public async Task FailedEdit_BlocksListSwitchWithoutPublishingPartialChanges()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var target = await service.CreateListAsync(Guid.NewGuid(), "目标清单");
        using var panel = await OpenPanel(service);
        var originalList = panel.SelectedListId;
        var row = await Add(panel, "原标题");
        await panel.BeginEditAsync(row);
        row.EditTitle = "待保存";
        using (var connection = storage.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER reject_title BEFORE UPDATE OF Title ON TodoTasks BEGIN SELECT RAISE(ABORT, 'test'); END;";
            command.ExecuteNonQuery();
        }

        Assert.False(await panel.SelectListAsync(target.Id));
        Assert.Equal(originalList, panel.SelectedListId);
        Assert.Same(row, panel.EditingTask);
        Assert.Equal("待保存", row.EditTitle);
        Assert.Equal("原标题", Assert.Single(panel.Store.Snapshot.Tasks).Title);
    }

    [Fact]
    public async Task RenameRetry_AfterSelectingAnotherList_UpdatesOnlyItsOriginalTarget()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var otherList = await service.CreateListAsync(Guid.NewGuid(), "清单 B");
        using var panel = await OpenPanel(service);
        var originalListId = panel.SelectedListId;
        Execute(storage, "CREATE TRIGGER reject_rename BEFORE UPDATE OF Name ON TaskLists BEGIN SELECT RAISE(ABORT, 'test'); END;");
        Assert.False(await panel.RenameListAsync("清单 A 新名称"));
        Assert.True(await panel.SelectListAsync(otherList.Id));
        Assert.True(panel.RetryCommand.CanExecute(null));
        Execute(storage, "DROP TRIGGER reject_rename;");

        Assert.True(await panel.RetryAsync());
        Assert.Equal(otherList.Id, panel.SelectedListId);
        Assert.Equal("清单 B", panel.ListName);
        var lists = (await service.GetSnapshotAsync()).Lists;
        Assert.Equal("清单 A 新名称", lists.Single(list => list.Id == originalListId).Name);
        Assert.Equal("清单 B", lists.Single(list => list.Id == otherList.Id).Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Import_ClearsOldUndoAndRetryOnlyWhenReplacementIsConfirmed(bool confirm)
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = new MainWindowViewModel(service, new TaskStore(await service.GetSnapshotAsync()), new FrozenClock());
        var row = await Add(panel, "导入后仍应删除");
        Assert.True(await panel.DeleteAsync(row));
        var path = Path.Combine(storage.RootPath, "deleted.json");
        await new JsonDataTransferService(service).ExportAsync(path);
        Assert.False(await panel.RenameListAsync(" "));
        Assert.True(panel.CanUndo);
        Assert.True(panel.RetryCommand.CanExecute(null));
        var management = new DataManagementViewModel(service, panel.Store);

        await management.ImportAsync(path, sqliteBackup: false, _ => confirm);

        Assert.Equal(!confirm, panel.CanUndo);
        Assert.Equal(!confirm, panel.UndoCommand.CanExecute(null));
        Assert.Equal(!confirm, panel.RetryCommand.CanExecute(null));
        Assert.Equal(!confirm, panel.HasError);
        if (confirm)
        {
            Assert.False(await panel.UndoAsync());
            Assert.True(await panel.RetryAsync());
            Assert.Equal(row.Id, Assert.Single(await service.GetDeletedTasksAsync()).Id);
        }
        else
        {
            Assert.True(await panel.UndoAsync());
            Assert.Equal(row.Id, Assert.Single(panel.ActiveTasks).Id);
        }
    }

    [Fact]
    public async Task RecentlyDeletedRestore_RefreshesEverySharedPanel()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        using var other = new MainWindowViewModel(service, panel.Store);
        var row = await Add(panel, "恢复原任务");
        await panel.DeleteAsync(row);
        var management = new DataManagementViewModel(service, panel.Store);
        await management.RefreshAsync();
        var deleted = Assert.Single(management.DeletedTasks);
        Assert.Equal(row.Id, deleted.Id);
        Assert.Equal(panel.ListName, deleted.ListName);
        Assert.Empty(other.ActiveTasks);

        await management.RestoreTaskAsync(deleted);
        Assert.Empty(management.DeletedTasks);
        Assert.Equal(row.Id, Assert.Single(panel.ActiveTasks).Id);
        Assert.Equal(row.Id, Assert.Single(other.ActiveTasks).Id);
        Assert.Empty(await service.GetDeletedTasksAsync());
        Assert.True(management.CanInteract);
    }

    [Fact]
    public async Task ExportAndCancelledImport_ShowPreviewWithoutReplacingCurrentData()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        var exported = await Add(panel, "导出内容");
        var management = new DataManagementViewModel(service, panel.Store);
        var path = Path.Combine(storage.RootPath, "preview.json");
        await management.ExportAsync(path);
        Assert.True(File.Exists(path));
        Assert.Contains("导出完成", management.Message);
        var current = await Add(panel, "导出后的任务");
        var confirmations = 0;

        await management.ImportAsync(path, sqliteBackup: false, preview =>
        {
            confirmations++;
            Assert.Equal(1, preview.ListCount);
            Assert.Equal(1, preview.TaskCount);
            Assert.Equal(exported.Id, Assert.Single(preview.Archive.Tasks).Id);
            return false;
        });

        Assert.Equal(1, confirmations);
        Assert.Equal(2, panel.PendingCount);
        Assert.Contains(panel.ActiveTasks, row => row.Id == current.Id);
        Assert.False(Directory.Exists(storage.Options.BackupDirectory));
        Assert.True(management.CanInteract);
    }

    [Fact]
    public async Task ConfirmedImport_ReplacesAllSharedViewsAndRefreshesRecentlyDeleted()
    {
        using var source = new TemporaryTaskTodoStorage();
        await using var sourceService = await source.OpenServiceAsync();
        var sourceList = Assert.Single((await sourceService.GetSnapshotAsync()).Lists);
        await sourceService.RenameListAsync(sourceList.Id, "导入清单");
        var active = await sourceService.CreateTaskAsync(Guid.NewGuid(), sourceList.Id, "导入待办");
        var deleted = await sourceService.CreateTaskAsync(Guid.NewGuid(), sourceList.Id, "导入删除记录");
        await sourceService.SoftDeleteTaskAsync(deleted.Id);
        await sourceService.SaveSettingsAsync(SettingsSnapshot.Default with { Theme = ThemePreference.Dark });
        var path = Path.Combine(source.RootPath, "import.json");
        await new JsonDataTransferService(sourceService).ExportAsync(path);

        using var target = new TemporaryTaskTodoStorage();
        await using var service = await target.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        using var other = new MainWindowViewModel(service, panel.Store);
        var old = await Add(panel, "被替换的旧任务");
        var management = new DataManagementViewModel(service, panel.Store);
        await management.ImportAsync(path, sqliteBackup: false, preview =>
        {
            Assert.Equal(2, preview.TaskCount);
            Assert.Equal(1, preview.DeletedTaskCount);
            return true;
        });

        Assert.Equal("导入清单", panel.ListName);
        Assert.Equal(active.Id, Assert.Single(panel.ActiveTasks).Id);
        Assert.Equal(active.Id, Assert.Single(other.ActiveTasks).Id);
        Assert.DoesNotContain(panel.Store.Snapshot.Tasks, task => task.Id == old.Id);
        Assert.Equal(deleted.Id, Assert.Single(management.DeletedTasks).Id);
        Assert.Equal(ThemePreference.Dark, panel.Store.Snapshot.Settings.Theme);
        Assert.Single(Directory.GetFiles(target.Options.BackupDirectory, "*.db"));
        Assert.Contains("数据已恢复", management.Message);
        Assert.Equal(ThemePreference.Dark, panel.Theme);
        Assert.Equal(ThemePreference.Dark, other.Theme);
        Assert.Equal(sourceList.Id, panel.SelectedListId);
    }

    [Fact]
    public async Task InvalidImport_ReportsErrorWithoutAskingForConfirmation()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        var original = await Add(panel, "保留");
        var management = new DataManagementViewModel(service, panel.Store);
        var path = Path.Combine(storage.RootPath, "invalid.json");
        await File.WriteAllTextAsync(path, "{ invalid");
        var asked = false;
        await management.ImportAsync(path, sqliteBackup: false, _ => { asked = true; return true; });

        Assert.False(asked);
        Assert.Equal(original.Id, Assert.Single(panel.ActiveTasks).Id);
        Assert.Contains("无效", management.Message);
        Assert.True(management.CanInteract);
    }

    [Fact]
    public async Task ConfirmedSqliteBackup_UsesSameReplacementAndSharedSnapshotPath()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        var backedUp = await Add(panel, "备份任务");
        var backup = await service.CreateBackupAsync();
        await Add(panel, "备份后的任务");
        var management = new DataManagementViewModel(service, panel.Store);
        await management.ImportAsync(backup, sqliteBackup: true, preview =>
        {
            Assert.Equal(1, preview.TaskCount);
            return true;
        });

        Assert.Equal(backedUp.Id, Assert.Single(panel.ActiveTasks).Id);
        Assert.Equal(backedUp.Id, Assert.Single((await service.GetSnapshotAsync()).Tasks).Id);
        Assert.Contains("数据已恢复", management.Message);
    }

    private static async Task<MainWindowViewModel> OpenPanel(TaskTodoService service)
        => new(service, new TaskStore(await service.GetSnapshotAsync()));

    private static async Task<TaskRowViewModel> Add(MainWindowViewModel panel, string title)
    {
        panel.NewTitle = title;
        Assert.True(await panel.AddAsync());
        return panel.ActiveTasks.Last();
    }

    private static void Execute(TemporaryTaskTodoStorage storage, string sql)
    {
        using var connection = storage.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
    }
}
