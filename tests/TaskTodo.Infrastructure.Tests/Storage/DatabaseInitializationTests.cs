using Microsoft.Data.Sqlite;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Backup;
using TaskTodo.Infrastructure.Migrations;
using TaskTodo.Infrastructure.Storage;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.Infrastructure.Tests.Storage;

public sealed class DatabaseInitializationTests
{
    [Fact]
    public async Task InitializeAndReopen_CreatesSchemaAndDoesNotRecreateRenamedInbox()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 20, 1, 2, 3, TimeSpan.Zero));
        Guid inboxId;

        await using (var service = await storage.OpenServiceAsync(clock))
        {
            var snapshot = await service.GetSnapshotAsync();
            var inbox = Assert.Single(snapshot.Lists);
            Assert.Equal("收件箱", inbox.Name);
            inboxId = inbox.Id;

            clock.Advance(TimeSpan.FromMinutes(1));
            var renamed = await service.RenameListAsync(inboxId, "我的清单");
            Assert.Equal("我的清单", renamed.Name);
            await service.FlushPendingWritesAsync();
        }

        await using (var reopened = await storage.OpenServiceAsync(clock))
        {
            var snapshot = await reopened.GetSnapshotAsync();
            var onlyList = Assert.Single(snapshot.Lists);
            Assert.Equal(inboxId, onlyList.Id);
            Assert.Equal("我的清单", onlyList.Name);
        }

        using var connection = storage.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(DatabaseSchema.CurrentVersion, ExecuteScalar<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(3, ExecuteScalar<long>(connection, """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name IN ('TaskLists', 'TodoTasks', 'Settings');
            """));
        Assert.True(ExecuteScalar<long>(connection, "SELECT COUNT(*) FROM pragma_foreign_key_list('TodoTasks');") > 0);
    }

    [Fact]
    public async Task VersionZeroDatabase_IsBackedUpThenMigrated_AndBackupOpensIndependently()
    {
        using var storage = new TemporaryTaskTodoStorage();
        using (var legacy = storage.OpenConnection(SqliteOpenMode.ReadWriteCreate))
        {
            ExecuteNonQuery(legacy, """
                CREATE TABLE LegacyProbe (Value TEXT NOT NULL);
                INSERT INTO LegacyProbe (Value) VALUES ('preserve-me');
                PRAGMA user_version = 0;
                """);
        }

        await using (var service = await storage.OpenServiceAsync())
        {
            Assert.Single((await service.GetSnapshotAsync()).Lists);
        }

        var backupPath = Assert.Single(Directory.GetFiles(storage.Options.BackupDirectory, "*.db"));
        using var backup = OpenDatabase(backupPath, SqliteOpenMode.ReadOnly);
        Assert.Equal(0, ExecuteScalar<long>(backup, "PRAGMA user_version;"));
        Assert.Equal("preserve-me", ExecuteScalar<string>(backup, "SELECT Value FROM LegacyProbe;"));
        Assert.Equal("ok", ExecuteScalar<string>(backup, "PRAGMA quick_check;"));

        using var migrated = storage.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(DatabaseSchema.CurrentVersion, ExecuteScalar<long>(migrated, "PRAGMA user_version;"));
        Assert.Equal("preserve-me", ExecuteScalar<string>(migrated, "SELECT Value FROM LegacyProbe;"));
    }

    [Fact]
    public async Task BackupFailure_BlocksMigrationAndLeavesVersionUnchanged()
    {
        using var storage = new TemporaryTaskTodoStorage();
        using (var legacy = storage.OpenConnection(SqliteOpenMode.ReadWriteCreate))
        {
            ExecuteNonQuery(legacy, "CREATE TABLE LegacyProbe (Value TEXT NOT NULL); PRAGMA user_version = 0;");
        }

        var exception = await Assert.ThrowsAsync<TaskTodoException>(
            () => storage.OpenServiceAsync(backupService: new FailingBackupService()));

        Assert.Equal(TaskTodoErrorCode.BackupFailed, exception.Code);
        using var unchanged = storage.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(0, ExecuteScalar<long>(unchanged, "PRAGMA user_version;"));
        Assert.Equal(0, ExecuteScalar<long>(unchanged, """
            SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'TaskLists';
            """));
    }

    [Fact]
    public async Task UnsupportedFutureVersion_IsRejectedWithoutReplacingDatabase()
    {
        using var storage = new TemporaryTaskTodoStorage();
        using (var future = storage.OpenConnection(SqliteOpenMode.ReadWriteCreate))
        {
            ExecuteNonQuery(future, """
                CREATE TABLE Sentinel (Value TEXT NOT NULL);
                INSERT INTO Sentinel (Value) VALUES ('still-here');
                PRAGMA user_version = 99;
                """);
        }

        var exception = await Assert.ThrowsAsync<TaskTodoException>(() => storage.OpenServiceAsync());

        Assert.Equal(TaskTodoErrorCode.UnsupportedDatabaseVersion, exception.Code);
        using var unchanged = storage.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(99, ExecuteScalar<long>(unchanged, "PRAGMA user_version;"));
        Assert.Equal("still-here", ExecuteScalar<string>(unchanged, "SELECT Value FROM Sentinel;"));
    }

    [Fact]
    public async Task CorruptExistingFile_IsRejectedAndNotOverwritten()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var originalBytes = "not-a-sqlite-database"u8.ToArray();
        await File.WriteAllBytesAsync(storage.Options.DatabasePath, originalBytes);

        var exception = await Assert.ThrowsAsync<TaskTodoException>(() => storage.OpenServiceAsync());

        Assert.Equal(TaskTodoErrorCode.DatabaseCorrupt, exception.Code);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(storage.Options.DatabasePath));
    }

    [Fact]
    public void DefaultPath_UsesCurrentUsersLocalApplicationDataWithoutOpeningIt()
    {
        var options = TaskTodoStorageOptions.CreateDefault();
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskTodo");

        Assert.Equal(Path.Combine(expectedRoot, "tasktodo.db"), options.DatabasePath);
        Assert.Equal(Path.Combine(expectedRoot, "Backups"), options.BackupDirectory);
    }

    [Fact]
    public void PermissionFailure_IsMappedToRecognizableAccessDeniedError()
    {
        var mapped = StorageExceptionMapper.Map(
            "open a temporary test database",
            new UnauthorizedAccessException("injected test failure"));

        Assert.Equal(TaskTodoErrorCode.StorageAccessDenied, mapped.Code);
        Assert.False(mapped.IsRetryable);
    }

    [Fact]
    public async Task Schema_EnforcesForeignKeyAndCompletionConstraintsAndCreatesIndexes()
    {
        using var storage = new TemporaryTaskTodoStorage();
        Guid listId;
        await using (var service = await storage.OpenServiceAsync())
        {
            listId = Assert.Single((await service.GetSnapshotAsync()).Lists).Id;
        }

        using var connection = storage.OpenConnection();
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
        var utc = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        var invalidCompletion = connection.CreateCommand();
        invalidCompletion.CommandText = """
            INSERT INTO TodoTasks (
                Id, ListId, Title, IsCompleted, SortOrder,
                CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, DeletedAtUtc)
            VALUES ($id, $listId, 'constraint probe', 0, 0, $utc, $utc, $utc, NULL);
            """;
        invalidCompletion.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        invalidCompletion.Parameters.AddWithValue("$listId", listId.ToString("D"));
        invalidCompletion.Parameters.AddWithValue("$utc", utc);
        Assert.Throws<SqliteException>(() => invalidCompletion.ExecuteNonQuery());

        var invalidForeignKey = connection.CreateCommand();
        invalidForeignKey.CommandText = """
            INSERT INTO TodoTasks (
                Id, ListId, Title, IsCompleted, SortOrder,
                CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, DeletedAtUtc)
            VALUES ($id, $listId, 'foreign key probe', 0, 0, $utc, $utc, NULL, NULL);
            """;
        invalidForeignKey.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        invalidForeignKey.Parameters.AddWithValue("$listId", Guid.NewGuid().ToString("D"));
        invalidForeignKey.Parameters.AddWithValue("$utc", utc);
        Assert.Throws<SqliteException>(() => invalidForeignKey.ExecuteNonQuery());

        Assert.Equal(2, ExecuteScalar<long>(connection, """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index'
              AND name IN ('IX_TodoTasks_List_Active_Order', 'IX_TodoTasks_DeletedAtUtc');
            """));
    }

    private static SqliteConnection OpenDatabase(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static T ExecuteScalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class FailingBackupService : ISqliteBackupService
    {
        public string CreateBackup(
            SqliteConnection sourceConnection,
            TaskTodoStorageOptions options,
            DateTimeOffset utcNow,
            string purpose)
            => throw new IOException("Injected backup failure for a temporary test database.");
    }
}
