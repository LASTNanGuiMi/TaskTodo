using System.Globalization;
using Microsoft.Data.Sqlite;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Backup;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.Infrastructure.Migrations;

internal sealed class DatabaseInitializer
{
    private const string MigrationV1Sql = """
        CREATE TABLE IF NOT EXISTS TaskLists (
            Id TEXT NOT NULL PRIMARY KEY,
            Name TEXT NOT NULL,
            SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            CONSTRAINT CK_TaskLists_Id CHECK (length(Id) = 36),
            CONSTRAINT CK_TaskLists_Name CHECK (length(trim(Name)) BETWEEN 1 AND 200)
        );

        CREATE TABLE IF NOT EXISTS TodoTasks (
            Id TEXT NOT NULL PRIMARY KEY,
            ListId TEXT NOT NULL,
            Title TEXT NOT NULL,
            IsCompleted INTEGER NOT NULL CHECK (IsCompleted IN (0, 1)),
            SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            CompletedAtUtc TEXT NULL,
            DeletedAtUtc TEXT NULL,
            CONSTRAINT FK_TodoTasks_TaskLists FOREIGN KEY (ListId) REFERENCES TaskLists(Id) ON DELETE RESTRICT,
            CONSTRAINT CK_TodoTasks_Id CHECK (length(Id) = 36),
            CONSTRAINT CK_TodoTasks_Title CHECK (length(trim(Title)) BETWEEN 1 AND 500),
            CONSTRAINT CK_TodoTasks_Completion CHECK (
                (IsCompleted = 0 AND CompletedAtUtc IS NULL)
                OR (IsCompleted = 1 AND CompletedAtUtc IS NOT NULL)
            )
        );

        CREATE TABLE IF NOT EXISTS Settings (
            Key TEXT NOT NULL PRIMARY KEY,
            JsonValue TEXT NOT NULL,
            CONSTRAINT CK_Settings_Key CHECK (length(trim(Key)) > 0),
            CONSTRAINT CK_Settings_JsonValue CHECK (length(JsonValue) > 0)
        );

        CREATE INDEX IF NOT EXISTS IX_TaskLists_Order
            ON TaskLists (SortOrder, Id);

        CREATE INDEX IF NOT EXISTS IX_TodoTasks_List_Active_Order
            ON TodoTasks (ListId, DeletedAtUtc, IsCompleted, SortOrder, Id);

        CREATE INDEX IF NOT EXISTS IX_TodoTasks_DeletedAtUtc
            ON TodoTasks (DeletedAtUtc, Id);
        """;

    private readonly TaskTodoStorageOptions _options;
    private readonly ISqliteBackupService _backupService;
    private readonly TimeProvider _timeProvider;
    private readonly bool _databaseExistedBeforeOpen;

    public DatabaseInitializer(
        TaskTodoStorageOptions options,
        ISqliteBackupService backupService,
        TimeProvider timeProvider,
        bool databaseExistedBeforeOpen)
    {
        _options = options;
        _backupService = backupService;
        _timeProvider = timeProvider;
        _databaseExistedBeforeOpen = databaseExistedBeforeOpen;
    }

    public void Initialize(SqliteConnection connection)
    {
        ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
        ExecutePragma(connection, "PRAGMA busy_timeout = 5000;");

        var currentVersion = GetUserVersion(connection);
        if (currentVersion > DatabaseSchema.CurrentVersion)
        {
            throw new TaskTodoException(
                TaskTodoErrorCode.UnsupportedDatabaseVersion,
                $"Database version {currentVersion} is newer than supported version {DatabaseSchema.CurrentVersion}.");
        }

        if (currentVersion < DatabaseSchema.CurrentVersion)
        {
            if (_databaseExistedBeforeOpen)
            {
                CreateRequiredMigrationBackup(connection, currentVersion);
            }

            ApplyMigrations(connection, currentVersion);
        }

        ExecutePragma(connection, "PRAGMA journal_mode = WAL;");
        ExecutePragma(connection, "PRAGMA synchronous = NORMAL;");
        EnsureDefaultInbox(connection);
    }

    private void CreateRequiredMigrationBackup(SqliteConnection connection, int currentVersion)
    {
        try
        {
            _backupService.CreateBackup(
                connection,
                _options,
                _timeProvider.GetUtcNow(),
                $"migration-v{currentVersion}-to-v{DatabaseSchema.CurrentVersion}");
        }
        catch (Exception exception)
        {
            throw new TaskTodoException(
                TaskTodoErrorCode.BackupFailed,
                "The pre-migration backup failed; the database was not migrated.",
                innerException: exception);
        }
    }

    private static void ApplyMigrations(SqliteConnection connection, int currentVersion)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            if (currentVersion < 1)
            {
                using var migrationCommand = connection.CreateCommand();
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = MigrationV1Sql;
                migrationCommand.ExecuteNonQuery();

                using var versionCommand = connection.CreateCommand();
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = "PRAGMA user_version = 1;";
                versionCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private void EnsureDefaultInbox(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            using var countCommand = connection.CreateCommand();
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM TaskLists;";
            var count = Convert.ToInt64(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

            if (count == 0)
            {
                var now = _timeProvider.GetUtcNow().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = """
                    INSERT INTO TaskLists (Id, Name, SortOrder, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ($id, $name, 0, $createdAtUtc, $updatedAtUtc);
                    """;
                insertCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                insertCommand.Parameters.AddWithValue("$name", "收件箱");
                insertCommand.Parameters.AddWithValue("$createdAtUtc", now);
                insertCommand.Parameters.AddWithValue("$updatedAtUtc", now);
                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void ExecutePragma(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
