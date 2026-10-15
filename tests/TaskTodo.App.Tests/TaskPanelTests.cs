using TaskTodo.App.State;
using TaskTodo.App.ViewModels;
using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

public sealed class TaskPanelTests
{
    [Fact]
    public async Task Add_normalizes_text_and_allows_duplicate_titles_with_distinct_ids()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        panel.NewTitle = "  中文\r\n任务 <tag>  ";
        Assert.True(await panel.AddAsync());
        Assert.Equal("中文 任务 <tag>", Assert.Single(panel.ActiveTasks).Title);
        Assert.Equal("", panel.NewTitle);
        panel.NewTitle = "中文 任务 <tag>";
        Assert.True(await panel.AddAsync());
        Assert.Equal(2, panel.PendingCount);
        Assert.Equal(2, panel.ActiveTasks.Select(row => row.Id).Distinct().Count());
        Assert.Equal(2, (await service.GetSnapshotAsync()).Tasks.Count);
    }

    [Fact]
    public async Task Failed_add_keeps_draft_and_committed_snapshot_then_retries_edited_input()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        Execute(storage, "CREATE TRIGGER fail_add BEFORE INSERT ON TodoTasks BEGIN SELECT RAISE(ABORT, 'test'); END;");
        panel.NewTitle = "第一次输入";
        Assert.False(await panel.AddAsync());
        Assert.Empty(panel.ActiveTasks);
        Assert.Equal("第一次输入", panel.NewTitle);
        Assert.True(panel.HasError);
        Assert.True(panel.CanInteract);
        Execute(storage, "DROP TRIGGER fail_add;");
        panel.NewTitle = "修正后的输入";
        Assert.True(await panel.RetryAsync());
        Assert.Equal("修正后的输入", Assert.Single(panel.ActiveTasks).Title);
        Assert.False(panel.HasError);
    }

    [Fact]
    public async Task Pending_add_does_not_publish_early_or_accept_a_duplicate_click()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        using var connection = storage.OpenConnection();
        using var transaction = connection.BeginTransaction();
        panel.NewTitle = "等待提交";
        var pending = panel.AddAsync();
        Assert.True(panel.IsSaving);
        Assert.Empty(panel.Store.Snapshot.Tasks);
        Assert.False(await panel.AddAsync());
        transaction.Commit();
        Assert.True(await pending);
        Assert.Single(panel.ActiveTasks);
    }

    [Fact]
    public async Task Editing_cancel_blank_and_valid_submission_keep_the_original_identity()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        var row = await Add(panel, "原标题");
        await panel.BeginEditAsync(row);
        row.EditTitle = "取消的编辑";
        panel.CancelEdit();
        Assert.Equal("原标题", row.Title);
        await panel.BeginEditAsync(row);
        row.EditTitle = " ";
        Assert.False(await panel.CommitEditAsync());
        Assert.True(row.IsEditing);
        Assert.Equal("原标题", row.Title);
        Assert.True(await panel.FinishEditingAsync());
        Assert.False(row.IsEditing);
        Assert.True(panel.HasError);
        await panel.BeginEditAsync(row);
        row.EditTitle = new string('中', 500);
        Assert.True(await panel.CommitEditAsync());
        Assert.Equal(row.Id, Assert.Single(panel.ActiveTasks).Id);
        Assert.Equal(500, row.Title.Length);
    }

    [Fact]
    public async Task Failed_edit_keeps_editor_and_original_title_until_retry_commits()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        var row = await Add(panel, "原文");
        await panel.BeginEditAsync(row);
        row.EditTitle = "修改内容";
        Execute(storage, "CREATE TRIGGER fail_edit BEFORE UPDATE OF Title ON TodoTasks BEGIN SELECT RAISE(ABORT, 'test'); END;");
        Assert.False(await panel.CommitEditAsync());
        Assert.Equal("原文", row.Title);
        Assert.Equal("修改内容", row.EditTitle);
        Assert.True(row.IsEditing);
        Execute(storage, "DROP TRIGGER fail_edit;");
        Assert.True(await panel.RetryAsync());
        Assert.Equal("修改内容", row.Title);
        Assert.Null(panel.EditingTask);
    }

    [Fact]
    public async Task Explicit_completion_updates_both_views_once_and_preserves_completion_time()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        using var otherView = new MainWindowViewModel(service, panel.Store);
        var row = await Add(panel, "共享快照");
        Assert.Equal(1, otherView.PendingCount);
        Assert.True(await panel.SetCompletedAsync(row, true));
        var completedAt = Assert.Single(panel.Store.Snapshot.Tasks).CompletedAtUtc;
        Assert.Empty(panel.ActiveTasks);
        Assert.Single(otherView.CompletedTasks);
        Assert.True(await panel.SetCompletedAsync(row, true));
        Assert.Equal(completedAt, Assert.Single(panel.Store.Snapshot.Tasks).CompletedAtUtc);
        Assert.True(await panel.SetCompletedAsync(row, false));
        Assert.Equal(1, otherView.PendingCount);
        Assert.Null(Assert.Single(panel.Store.Snapshot.Tasks).CompletedAtUtc);
    }

    [Fact]
    public async Task Delete_and_undo_restore_same_id_and_expire_after_eight_seconds()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var clock = new TestClock();
        await using var service = await storage.OpenServiceAsync(clock);
        using var panel = await OpenPanel(service, clock);
        var row = await Add(panel, "可恢复");
        Assert.True(await panel.DeleteAsync(row));
        Assert.Empty(panel.ActiveTasks);
        Assert.True(panel.CanUndo);
        Assert.True(await panel.UndoAsync());
        Assert.Equal(row.Id, Assert.Single(panel.ActiveTasks).Id);
        await panel.DeleteAsync(panel.ActiveTasks[0]);
        clock.Advance(TimeSpan.FromSeconds(8));
        panel.RefreshUndo();
        Assert.False(panel.CanUndo);
        Assert.False(await panel.UndoAsync());
        Assert.Empty(panel.ActiveTasks);
        using var connection = storage.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TodoTasks WHERE DeletedAtUtc IS NOT NULL;";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Reorder_persists_complete_active_order_and_keeps_completed_sort_key()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using (var service = await storage.OpenServiceAsync())
        {
            using var panel = await OpenPanel(service);
            var a = await Add(panel, "A");
            var b = await Add(panel, "B");
            var c = await Add(panel, "C");
            await panel.SetCompletedAsync(b, true);
            var completedOrder = panel.CompletedTasks[0].SortOrder;
            Assert.True(await panel.MoveAsync(c.Id, a.Id, after: false));
            Assert.Equal(["C", "A"], panel.ActiveTasks.Select(row => row.Title));
            Assert.Equal(completedOrder, panel.CompletedTasks[0].SortOrder);
            Assert.False(await panel.MoveAsync(b.Id, a.Id, after: false));
        }
        await using var reopened = await storage.OpenServiceAsync();
        using var restored = await OpenPanel(reopened);
        Assert.Equal(["C", "A"], restored.ActiveTasks.Select(row => row.Title));
        Assert.Equal("B", Assert.Single(restored.CompletedTasks).Title);
    }

    [Fact]
    public async Task Reorder_failure_keeps_the_confirmed_visual_order()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        var a = await Add(panel, "A");
        var b = await Add(panel, "B");
        Execute(storage, "CREATE TRIGGER fail_order BEFORE UPDATE OF SortOrder ON TodoTasks BEGIN SELECT RAISE(ABORT, 'test'); END;");
        Assert.False(await panel.MoveAsync(b.Id, a.Id, after: false));
        Assert.Equal(["A", "B"], panel.ActiveTasks.Select(row => row.Title));
        Assert.True(panel.HasError);
    }

    [Fact]
    public async Task Settings_are_published_after_save_and_survive_reopen()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using (var service = await storage.OpenServiceAsync())
        {
            using var panel = await OpenPanel(service);
            panel.BackgroundOpacity = 0.55;
            panel.Theme = ThemePreference.Dark;
            panel.IsAlwaysOnTop = false;
            panel.RememberWindow(new("test-monitor", new WindowBounds(80, 90, 360, 480)),
                WindowPresentationState.Docked);
            Assert.Equal(SettingsSnapshot.Default, panel.Store.Snapshot.Settings);
            Assert.True(await panel.SaveSettingsAsync());
        }
        await using var reopened = await storage.OpenServiceAsync();
        using var restored = await OpenPanel(reopened);
        Assert.Equal(0.55, restored.BackgroundOpacity);
        Assert.Equal(ThemePreference.Dark, restored.Theme);
        Assert.False(restored.IsAlwaysOnTop);
        Assert.Equal(WindowPresentationState.Docked, restored.Settings.LastPresentationState);
        Assert.Equal(new WindowBounds(80, 90, 360, 480), restored.Settings.WindowPlacement!.Bounds);
    }

    [Fact]
    public async Task Glass_material_paints_with_the_opacity_setting_and_solid_stays_opaque()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        Assert.Equal(1.0, panel.SurfaceOpacity);
        panel.BackgroundOpacity = 1.0;
        panel.Material = SurfaceMaterial.Clear;
        Assert.True(panel.IsTranslucent);
        Assert.Equal(MainWindowViewModel.ClearDefaultOpacity, panel.SurfaceOpacity);
        panel.Material = SurfaceMaterial.Glass;
        Assert.Equal(MainWindowViewModel.GlassDefaultOpacity, panel.SurfaceOpacity);
        panel.BackgroundOpacity = 0.5;
        Assert.Equal(0.5, panel.SurfaceOpacity);
        panel.Palette = ColorPalette.Amber;
        Assert.True(await panel.SaveSettingsAsync());
        panel.Material = SurfaceMaterial.Solid;
        Assert.Equal(1.0, panel.SurfaceOpacity);
        Assert.Equal(0.5, panel.BackgroundOpacity);
        Assert.Equal(ColorPalette.Amber, panel.Store.Snapshot.Settings.Palette);
        Assert.Equal(SurfaceMaterial.Glass, panel.Store.Snapshot.Settings.Material);
    }

    [Fact]
    public async Task Failed_settings_save_retains_draft_and_can_retry()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        Execute(storage, "CREATE TRIGGER fail_settings BEFORE INSERT ON Settings BEGIN SELECT RAISE(ABORT, 'test'); END;");
        panel.BackgroundOpacity = 0.4;
        Assert.False(await panel.SaveSettingsAsync());
        Assert.Equal(0.88, panel.Store.Snapshot.Settings.BackgroundOpacity);
        Assert.Equal(0.4, panel.BackgroundOpacity);
        Execute(storage, "DROP TRIGGER fail_settings;");
        Assert.True(await panel.RetryAsync());
        Assert.Equal(0.4, panel.Store.Snapshot.Settings.BackgroundOpacity);
    }

    [Fact]
    public async Task Exit_commits_valid_editor_quick_add_and_settings_before_reopen()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using (var service = await storage.OpenServiceAsync())
        {
            using var panel = await OpenPanel(service);
            var row = await Add(panel, "旧标题");
            await panel.BeginEditAsync(row);
            row.EditTitle = "退出前编辑";
            panel.NewTitle = "退出前新增";
            panel.Theme = ThemePreference.Light;
            Assert.True(await panel.PrepareExitAsync());
            Assert.True(panel.IsClosing);
            Assert.Empty(panel.NewTitle);
            await service.StopAsync();
        }
        await using var reopened = await storage.OpenServiceAsync();
        using var restored = await OpenPanel(reopened);
        Assert.Equal(["退出前编辑", "退出前新增"], restored.ActiveTasks.Select(row => row.Title));
        Assert.Equal(ThemePreference.Light, restored.Theme);
    }

    [Fact]
    public async Task Exit_failure_keeps_application_editable_and_preserves_unsaved_input()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        using var panel = await OpenPanel(service);
        panel.NewTitle = new string('x', 501);
        Assert.False(await panel.PrepareExitAsync());
        Assert.False(panel.IsClosing);
        Assert.True(panel.CanInteract);
        Assert.Equal(501, panel.NewTitle.Length);
        Assert.True(panel.HasError);
        panel.NewTitle = "修正后退出";
        Assert.True(await panel.PrepareExitAsync());
        Assert.Equal("修正后退出", Assert.Single((await service.GetSnapshotAsync()).Tasks).Title);
    }

    private static async Task<MainWindowViewModel> OpenPanel(TaskTodoService service, TimeProvider? clock = null)
        => new(service, new TaskStore(await service.GetSnapshotAsync()), clock);

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

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
