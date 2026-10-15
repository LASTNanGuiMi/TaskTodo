using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Backup;
using TaskTodo.Infrastructure.Storage;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.Infrastructure.Tests.Storage;

public sealed class DataRecoveryTests
{
    [Fact]
    public async Task JsonRoundTrip_PreservesAllRecordsAndLocalWindowSettingsWithPreImportBackup()
    {
        using var source = new TemporaryTaskTodoStorage();
        await using var sourceService = await source.OpenServiceAsync();
        var inbox = Assert.Single((await sourceService.GetSnapshotAsync()).Lists);
        var list = await sourceService.CreateListAsync(Guid.NewGuid(), "项目清单");
        await sourceService.CreateTaskAsync(Guid.NewGuid(), inbox.Id, "未完成");
        var completed = await sourceService.CreateTaskAsync(Guid.NewGuid(), list.Id, "已完成");
        await sourceService.SetCompletedAsync(completed.Id, true);
        var deleted = await sourceService.CreateTaskAsync(Guid.NewGuid(), list.Id, "已删除");
        await sourceService.SoftDeleteTaskAsync(deleted.Id);
        await sourceService.SaveSettingsAsync(SettingsSnapshot.Default with
        {
            Theme = ThemePreference.Dark,
            WindowPlacement = new WindowPlacementSnapshot("foreign-monitor", new WindowBounds(-5000, 0, 400, 500)),
        });

        var exportPath = Path.Combine(source.RootPath, "export.json");
        await new JsonDataTransferService(sourceService).ExportAsync(exportPath);
        var json = await File.ReadAllTextAsync(exportPath);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.DoesNotContain("foreign-monitor", json);
        Assert.DoesNotContain("windowPlacement", json);

        using var target = new TemporaryTaskTodoStorage();
        await using var targetService = await target.OpenServiceAsync();
        var targetInbox = Assert.Single((await targetService.GetSnapshotAsync()).Lists);
        var oldTask = await targetService.CreateTaskAsync(Guid.NewGuid(), targetInbox.Id, "原数据");
        var localSettings = SettingsSnapshot.Default with
        {
            WindowPlacement = new WindowPlacementSnapshot("local-monitor", new WindowBounds(20, 30, 340, 440)),
            DockSide = DockSide.Left,
            DockOffset = 0.7,
            LastPresentationState = WindowPresentationState.Docked,
            SelectedListId = targetInbox.Id,
        };
        await targetService.SaveSettingsAsync(localSettings);
        var transfer = new JsonDataTransferService(targetService);
        var preview = await transfer.PreviewImportAsync(exportPath);
        Assert.Equal(2, preview.ListCount);
        Assert.Equal(3, preview.TaskCount);
        Assert.Equal(1, preview.CompletedTaskCount);
        Assert.Equal(1, preview.DeletedTaskCount);
        Assert.Equal(oldTask.Id, Assert.Single((await targetService.GetSnapshotAsync()).Tasks).Id);

        var backupPath = await transfer.ImportAsync(preview);
        var beforeImport = await SqliteRecoveryService.ReadBackupAsync(backupPath);
        Assert.Equal(oldTask.Id, Assert.Single(beforeImport.Archive.Tasks).Id);
        var imported = await targetService.GetArchiveAsync();
        Assert.Equal(preview.Archive.Lists, imported.Lists);
        Assert.Equal(preview.Archive.Tasks, imported.Tasks);
        var afterSettings = (await targetService.GetSnapshotAsync()).Settings;
        Assert.Equal(localSettings.WindowPlacement, afterSettings.WindowPlacement);
        Assert.Equal(localSettings.DockSide, afterSettings.DockSide);
        Assert.Equal(localSettings.DockOffset, afterSettings.DockOffset);
        Assert.Equal(localSettings.LastPresentationState, afterSettings.LastPresentationState);
        Assert.Equal(ThemePreference.Dark, afterSettings.Theme);
        Assert.Equal(preview.Archive.Lists[0].Id, afterSettings.SelectedListId);
    }

    [Fact]
    public async Task ExportFailure_ReportsStorageErrorAndRemovesPartialFile()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var destination = Path.Combine(storage.RootPath, "is-a-directory.json");
        Directory.CreateDirectory(destination);
        await Assert.ThrowsAsync<TaskTodoException>(() => new JsonDataTransferService(service).ExportAsync(destination));
        Assert.True(Directory.Exists(destination));
        Assert.Empty(Directory.GetFiles(storage.RootPath, "*.tmp"));
    }

    [Fact]
    public async Task RecentDeletes_SurviveReopenAndRestoreSameIdentity()
    {
        using var storage = new TemporaryTaskTodoStorage();
        TodoTask deleted;
        await using (var service = await storage.OpenServiceAsync())
        {
            var inbox = Assert.Single((await service.GetSnapshotAsync()).Lists);
            var task = await service.CreateTaskAsync(Guid.NewGuid(), inbox.Id, "稍后恢复");
            deleted = await service.SoftDeleteTaskAsync(task.Id);
            Assert.Empty((await service.GetSnapshotAsync()).Tasks);
        }

        await using var reopened = await storage.OpenServiceAsync();
        Assert.Equal(deleted, Assert.Single(await reopened.GetDeletedTasksAsync()));
        var restored = await reopened.RestoreTaskAsync(deleted.Id);
        Assert.Equal(deleted.Id, restored.Id);
        Assert.Equal(deleted.Title, restored.Title);
        Assert.Equal(deleted.SortOrder, restored.SortOrder);
        Assert.Empty(await reopened.GetDeletedTasksAsync());
        Assert.Equal(restored, Assert.Single((await reopened.GetSnapshotAsync()).Tasks));
    }

    [Theory]
    [InlineData("future-version")]
    [InlineData("missing-version")]
    [InlineData("no-lists")]
    [InlineData("duplicate-list")]
    [InlineData("duplicate-task")]
    [InlineData("missing-reference")]
    [InlineData("empty-id")]
    [InlineData("empty-title")]
    [InlineData("long-title")]
    [InlineData("completion-mismatch")]
    [InlineData("invalid-opacity")]
    [InlineData("invalid-theme")]
    [InlineData("negative-order")]
    [InlineData("missing-time")]
    [InlineData("null-task")]
    [InlineData("null-settings")]
    public async Task InvalidImport_IsRejectedBeforeChangingData(string error)
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var inbox = Assert.Single((await service.GetSnapshotAsync()).Lists);
        var task = await service.CreateTaskAsync(Guid.NewGuid(), inbox.Id, "保留旧数据");
        var transfer = new JsonDataTransferService(service);
        var path = Path.Combine(storage.RootPath, "invalid.json");
        await transfer.ExportAsync(path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var tasks = json["tasks"]!.AsArray();
        switch (error)
        {
            case "future-version": json["schemaVersion"] = 999; break;
            case "missing-version": json.Remove("schemaVersion"); break;
            case "no-lists": json["lists"] = new JsonArray(); break;
            case "duplicate-list": json["lists"]!.AsArray().Add(json["lists"]![0]!.DeepClone()); break;
            case "duplicate-task": tasks.Add(tasks[0]!.DeepClone()); break;
            case "missing-reference": tasks[0]!["listId"] = Guid.NewGuid(); break;
            case "empty-id": tasks[0]!["id"] = Guid.Empty; break;
            case "empty-title": tasks[0]!["title"] = " "; break;
            case "long-title": tasks[0]!["title"] = new string('x', 501); break;
            case "completion-mismatch": tasks[0]!["isCompleted"] = true; break;
            case "invalid-opacity": json["settings"]!["backgroundOpacity"] = 0.1; break;
            case "invalid-theme": json["settings"]!["theme"] = "Unknown"; break;
            case "negative-order": tasks[0]!["sortOrder"] = -1; break;
            case "missing-time": tasks[0]!.AsObject().Remove("createdAtUtc"); break;
            case "null-task": tasks.Add((JsonNode?)null); break;
            case "null-settings": json["settings"] = null; break;
        }

        await File.WriteAllTextAsync(path, json.ToJsonString());
        var failure = await Assert.ThrowsAsync<TaskTodoException>(() => transfer.PreviewImportAsync(path));
        Assert.Equal(TaskTodoErrorCode.Validation, failure.Code);
        Assert.Equal(task, Assert.Single((await service.GetSnapshotAsync()).Tasks));
        Assert.False(Directory.Exists(storage.Options.BackupDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedOrOversizedImport_IsRejected(bool oversized)
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var path = Path.Combine(storage.RootPath, "bad.json");
        if (oversized)
        {
            await using var file = File.Create(path);
            file.SetLength(JsonDataTransferService.MaximumImportBytes + 1);
        }
        else
        {
            await File.WriteAllTextAsync(path, "{broken-json");
        }

        var failure = await Assert.ThrowsAsync<TaskTodoException>(() => new JsonDataTransferService(service).PreviewImportAsync(path));
        Assert.Equal(TaskTodoErrorCode.Validation, failure.Code);
        Assert.Single((await service.GetSnapshotAsync()).Lists);
    }

    [Fact]
    public async Task ReplacementFailure_RollsBackAllListsTasksAndSettings()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var inbox = Assert.Single((await service.GetSnapshotAsync()).Lists);
        await service.CreateTaskAsync(Guid.NewGuid(), inbox.Id, "保留");
        var original = await service.GetArchiveAsync();
        using (var connection = storage.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER RejectImport BEFORE INSERT ON TodoTasks
                BEGIN SELECT RAISE(ABORT, 'injected import failure'); END;
                """;
            command.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<TaskTodoException>(() => service.ReplaceDataAsync(original));
        var after = await service.GetArchiveAsync();
        Assert.Equal(original.Lists, after.Lists);
        Assert.Equal(original.Tasks, after.Tasks);
        Assert.Equal(original.Settings, after.Settings);
        Assert.Single(Directory.GetFiles(storage.Options.BackupDirectory, "*.db"));
    }

    [Fact]
    public async Task BackupFailure_BlocksReplacement()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync(backupService: new FailingBackup());
        var original = await service.GetArchiveAsync();
        var failure = await Assert.ThrowsAsync<TaskTodoException>(() => service.ReplaceDataAsync(original));
        Assert.Equal(TaskTodoErrorCode.BackupFailed, failure.Code);
        Assert.Equal(original.Lists, (await service.GetArchiveAsync()).Lists);
    }

    [Fact]
    public async Task RestoreDamagedDatabase_PreservesOriginalBytesAndSidecars()
    {
        using var storage = new TemporaryTaskTodoStorage();
        string backupPath;
        Guid taskId;
        await using (var service = await storage.OpenServiceAsync())
        {
            var inbox = Assert.Single((await service.GetSnapshotAsync()).Lists);
            taskId = (await service.CreateTaskAsync(Guid.NewGuid(), inbox.Id, "已备份")).Id;
            backupPath = await service.CreateBackupAsync();
        }

        byte[] corrupt = [1, 3, 3, 7, 19];
        await File.WriteAllBytesAsync(storage.Options.DatabasePath, corrupt);
        await File.WriteAllBytesAsync(storage.Options.DatabasePath + "-wal", [8, 9]);
        await File.WriteAllBytesAsync(storage.Options.DatabasePath + "-shm", [10, 11]);
        var preserved = await SqliteRecoveryService.RestoreAsync(storage.Options, backupPath);
        Assert.NotNull(preserved);
        Assert.Equal(corrupt, await File.ReadAllBytesAsync(Path.Combine(preserved, "tasktodo.db")));
        Assert.Equal(new byte[] { 8, 9 }, await File.ReadAllBytesAsync(Path.Combine(preserved, "tasktodo.db-wal")));
        Assert.Equal(new byte[] { 10, 11 }, await File.ReadAllBytesAsync(Path.Combine(preserved, "tasktodo.db-shm")));
        await using var reopened = await storage.OpenServiceAsync();
        var snapshot = await reopened.GetSnapshotAsync();
        Assert.Equal(taskId, Assert.Single(snapshot.Tasks).Id);
        Assert.Null(snapshot.Settings.WindowPlacement);
        Assert.Equal(WindowPresentationState.Expanded, snapshot.Settings.LastPresentationState);
    }

    [Fact]
    public async Task InvalidBackup_DoesNotTouchDamagedOriginal()
    {
        using var storage = new TemporaryTaskTodoStorage();
        byte[] original = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(storage.Options.DatabasePath, original);
        var badBackup = Path.Combine(storage.RootPath, "invalid.db");
        await File.WriteAllBytesAsync(badBackup, [7, 8, 9]);
        await Assert.ThrowsAsync<TaskTodoException>(() => SqliteRecoveryService.RestoreAsync(storage.Options, badBackup));
        Assert.Equal(original, await File.ReadAllBytesAsync(storage.Options.DatabasePath));
        Assert.Empty(Directory.GetDirectories(storage.RootPath, "RecoveryOriginal-*"));
    }

    [Fact]
    public async Task BackupRotation_KeepsTenUsableNewestFiles()
    {
        using var storage = new TemporaryTaskTodoStorage();
        await using var service = await storage.OpenServiceAsync();
        var created = new List<string>();
        for (var index = 0; index < 12; index++)
        {
            created.Add(await service.CreateBackupAsync());
        }

        var files = Directory.GetFiles(storage.Options.BackupDirectory, "*.db");
        Assert.Equal(10, files.Length);
        Assert.Contains(created[^1], files);
        foreach (var file in files)
        {
            Assert.Single((await SqliteRecoveryService.ReadBackupAsync(file)).Archive.Lists);
        }
    }

    private sealed class FailingBackup : ISqliteBackupService
    {
        public string CreateBackup(SqliteConnection sourceConnection, TaskTodoStorageOptions options, DateTimeOffset utcNow, string purpose)
            => throw new IOException("Injected backup failure.");
    }
}
