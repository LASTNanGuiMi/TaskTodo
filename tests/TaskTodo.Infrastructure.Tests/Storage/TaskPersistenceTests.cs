using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.Infrastructure.Tests.Storage;

public sealed class TaskPersistenceTests
{
    [Fact]
    public async Task CreateTask_NormalizesPlainText_AllowsDuplicates_AndPersistsAfterReopen()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 20, 2, 0, 0, TimeSpan.Zero));
        Guid listId;
        Guid firstId;
        Guid secondId;

        await using (var service = await storage.OpenServiceAsync(clock))
        {
            listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
            firstId = Guid.NewGuid();
            secondId = Guid.NewGuid();

            var first = await service.CreateTaskAsync(firstId, listId, "  同名\r\n任务  ");
            var second = await service.CreateTaskAsync(secondId, listId, "同名 任务");

            Assert.Equal("同名 任务", first.Title);
            Assert.Equal("同名 任务", second.Title);
            Assert.NotEqual(first.Id, second.Id);
            await service.FlushPendingWritesAsync();
        }

        await using var reopened = await storage.OpenServiceAsync(clock);
        var snapshot = await reopened.GetSnapshotAsync();
        Assert.Equal(2, snapshot.Tasks.Count);
        Assert.Contains(snapshot.Tasks, task => task.Id == firstId && task.Title == "同名 任务");
        Assert.Contains(snapshot.Tasks, task => task.Id == secondId && task.Title == "同名 任务");
        Assert.Equal(2, snapshot.GetIncompleteCount(listId));
    }

    [Fact]
    public async Task TitleBoundaries_RejectBlankAndOver500_AndAcceptExactly500()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;

        var blank = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.CreateTaskAsync(Guid.NewGuid(), listId, " \r\n\t "));
        var tooLong = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.CreateTaskAsync(Guid.NewGuid(), listId, new string('字', 501)));
        var accepted = await service.CreateTaskAsync(Guid.NewGuid(), listId, new string('字', 500));

        Assert.Equal(TaskTodoErrorCode.Validation, blank.Code);
        Assert.Equal(TaskTodoErrorCode.Validation, tooLong.Code);
        Assert.Equal(500, accepted.Title.Length);
        Assert.Single((await service.GetSnapshotAsync()).Tasks);
    }

    [Fact]
    public async Task CreateRetryWithSameRequestId_IsIdempotent_AndConflictingReuseIsRejected()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        var requestId = Guid.NewGuid();

        var firstAttempt = service.CreateTaskAsync(requestId, listId, "一次提交");
        var retriedAttempt = service.CreateTaskAsync(requestId, listId, "一次提交");
        var results = await Task.WhenAll(firstAttempt, retriedAttempt);

        Assert.Equal(results[0], results[1]);
        Assert.Single((await service.GetSnapshotAsync()).Tasks);

        var conflict = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.CreateTaskAsync(requestId, listId, "不同内容"));
        Assert.Equal(TaskTodoErrorCode.Conflict, conflict.Code);
        Assert.Single((await service.GetSnapshotAsync()).Tasks);
    }

    [Fact]
    public async Task CreateRetryAfterCommittedReopen_ReturnsOriginalTaskWithoutDuplicating()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var requestId = Guid.NewGuid();
        Guid listId;

        await using (var firstSession = await storage.OpenServiceAsync())
        {
            listId = Assert.Single((await firstSession.GetSnapshotAsync()).Lists).Id;
            await firstSession.CreateTaskAsync(requestId, listId, "可安全重试");
            await firstSession.FlushPendingWritesAsync();
        }

        await using var retriedSession = await storage.OpenServiceAsync();
        var retried = await retriedSession.CreateTaskAsync(requestId, listId, "可安全重试");
        Assert.Equal(requestId, retried.Id);
        Assert.Single((await retriedSession.GetSnapshotAsync()).Tasks);
    }

    [Fact]
    public async Task InvalidEntityIds_ReturnRecognizableErrorsWithoutDirtyData()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();

        var emptyId = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.CreateTaskAsync(Guid.Empty, Guid.NewGuid(), "任务"));
        var missingList = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.CreateTaskAsync(Guid.NewGuid(), Guid.NewGuid(), "任务"));
        var missingTask = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.SetCompletedAsync(Guid.NewGuid(), true));
        var missingRename = await Assert.ThrowsAsync<TaskTodoException>(
            () => service.RenameListAsync(Guid.NewGuid(), "不存在"));

        Assert.Equal(TaskTodoErrorCode.Validation, emptyId.Code);
        Assert.Equal(TaskTodoErrorCode.NotFound, missingList.Code);
        Assert.Equal(TaskTodoErrorCode.NotFound, missingTask.Code);
        Assert.Equal(TaskTodoErrorCode.NotFound, missingRename.Code);
        Assert.Empty((await service.GetSnapshotAsync()).Tasks);
    }

    [Fact]
    public async Task CreateAndRenameList_PersistWithIndependentIdsAndUtcTimes()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 20, 3, 0, 0, TimeSpan.Zero));
        var listId = Guid.NewGuid();

        await using (var service = await storage.OpenServiceAsync(clock))
        {
            var created = await service.CreateListAsync(listId, "  工作\n清单 ");
            Assert.Equal("工作 清单", created.Name);
            Assert.Equal(TimeSpan.Zero, created.CreatedAtUtc.Offset);

            clock.Advance(TimeSpan.FromMinutes(2));
            var renamed = await service.RenameListAsync(listId, "项目");
            Assert.Equal("项目", renamed.Name);
            Assert.Equal(created.CreatedAtUtc, renamed.CreatedAtUtc);
            Assert.True(renamed.UpdatedAtUtc > created.UpdatedAtUtc);
        }

        await using var reopened = await storage.OpenServiceAsync(clock);
        Assert.Contains((await reopened.GetSnapshotAsync()).Lists, list => list.Id == listId && list.Name == "项目");
    }

    [Fact]
    public async Task Settings_AreValidatedSavedAndReopened()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var expected = new SettingsSnapshot(
            BackgroundOpacity: 0.63,
            IsAlwaysOnTop: false,
            Theme: ThemePreference.Dark,
            WindowPlacement: new WindowPlacementSnapshot(
                "DISPLAY-2",
                new WindowBounds(-1800, 40, 420, 560)),
            DockSide: DockSide.Left,
            DockOffset: 0.72,
            LastPresentationState: WindowPresentationState.Docked);

        await using (var service = await storage.OpenServiceAsync())
        {
            await service.SaveSettingsAsync(expected);

            var invalid = expected with { BackgroundOpacity = 0.10 };
            var exception = await Assert.ThrowsAsync<TaskTodoException>(
                () => service.SaveSettingsAsync(invalid));
            Assert.Equal(TaskTodoErrorCode.Validation, exception.Code);
        }

        await using var reopened = await storage.OpenServiceAsync();
        Assert.Equal(expected, (await reopened.GetSnapshotAsync()).Settings);
    }

    [Fact]
    public async Task CollapseStyle_DefaultsToCapsuleAndPersistsOrbChoice()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using (var service = await storage.OpenServiceAsync())
        {
            Assert.Equal(CollapseStyle.Capsule, (await service.GetSnapshotAsync()).Settings.CollapseStyle);
            await service.SaveSettingsAsync((await service.GetSnapshotAsync()).Settings with { CollapseStyle = CollapseStyle.Orb });
        }

        await using var reopened = await storage.OpenServiceAsync();
        Assert.Equal(CollapseStyle.Orb, (await reopened.GetSnapshotAsync()).Settings.CollapseStyle);
    }

    [Fact]
    public async Task PaletteAndMaterial_DefaultToIndigoSolidAndPersistChoice()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using (var service = await storage.OpenServiceAsync())
        {
            var settings = (await service.GetSnapshotAsync()).Settings;
            Assert.Equal(ColorPalette.Indigo, settings.Palette);
            Assert.Equal(SurfaceMaterial.Solid, settings.Material);
            await service.SaveSettingsAsync(settings with { Palette = ColorPalette.Rose, Material = SurfaceMaterial.Glass });
            var invalid = settings with { Palette = (ColorPalette)42 };
            var exception = await Assert.ThrowsAsync<TaskTodoException>(() => service.SaveSettingsAsync(invalid));
            Assert.Equal(TaskTodoErrorCode.Validation, exception.Code);
        }

        await using var reopened = await storage.OpenServiceAsync();
        var restored = (await reopened.GetSnapshotAsync()).Settings;
        Assert.Equal(ColorPalette.Rose, restored.Palette);
        Assert.Equal(SurfaceMaterial.Glass, restored.Material);
    }

    [Fact]
    public async Task SemanticallyInvalidStoredSettings_AreReportedInsteadOfSilentlyDefaulted()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using (var initialized = await storage.OpenServiceAsync())
        {
            await initialized.FlushPendingWritesAsync();
        }

        using (var connection = storage.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Settings (Key, JsonValue)
                VALUES ('application', $json)
                ON CONFLICT(Key) DO UPDATE SET JsonValue = excluded.JsonValue;
                """;
            command.Parameters.AddWithValue(
                "$json",
                """{"backgroundOpacity":0.1,"isAlwaysOnTop":true,"theme":"System","windowPlacement":null,"dockSide":"Right","dockOffset":0.2,"lastPresentationState":"Expanded"}""");
            command.ExecuteNonQuery();
        }

        await using var reopened = await storage.OpenServiceAsync();
        var exception = await Assert.ThrowsAsync<TaskTodoException>(() => reopened.GetSnapshotAsync());
        Assert.Equal(TaskTodoErrorCode.InvalidStoredData, exception.Code);
    }
}
