using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Backup;
using TaskTodo.Infrastructure.Migrations;

namespace TaskTodo.Infrastructure.Storage;

public sealed partial class SqliteTaskTodoRepository : ITaskTodoRepository
{
    private const string ApplicationSettingsKey = "application";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TaskTodoStorageOptions _options;
    private readonly ISqliteBackupService _backupService;
    private readonly SerialDatabaseWorker _worker;

    internal int WorkerManagedThreadId => _worker.WorkerManagedThreadId;

    private SqliteTaskTodoRepository(
        TaskTodoStorageOptions options,
        ISqliteBackupService backupService,
        TimeProvider timeProvider,
        bool databaseExistedBeforeOpen)
    {
        _options = options;
        _backupService = backupService;
        var initializer = new DatabaseInitializer(
            options,
            backupService,
            timeProvider,
            databaseExistedBeforeOpen);
        _worker = new SerialDatabaseWorker(
            () => new SqliteConnection(options.BuildConnectionString()),
            connection => StorageExceptionMapper.Execute(
                "initialize the database",
                () => initializer.Initialize(connection)));
    }

    public static async Task<SqliteTaskTodoRepository> OpenAsync(
        TaskTodoStorageOptions options,
        TimeProvider? timeProvider = null,
        ISqliteBackupService? backupService = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var databaseDirectory = Path.GetDirectoryName(options.DatabasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectory))
        {
            throw new TaskTodoException(TaskTodoErrorCode.Validation, "Database path has no parent directory.");
        }

        StorageExceptionMapper.Execute(
            "prepare the database directory",
            () => Directory.CreateDirectory(databaseDirectory));
        var databaseExistedBeforeOpen = File.Exists(options.DatabasePath);
        var repository = new SqliteTaskTodoRepository(
            options,
            backupService ?? new SqliteBackupService(),
            timeProvider ?? TimeProvider.System,
            databaseExistedBeforeOpen);

        try
        {
            await repository._worker.StartAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return repository;
        }
        catch
        {
            await repository._worker.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<TodoTask> CreateTaskAsync(
        Guid requestId,
        Guid listId,
        string normalizedTitle,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "create a task",
            connection => InTransaction(connection, transaction =>
            {
                var existing = ReadTaskById(connection, transaction, requestId, includeDeleted: true);
                if (existing is not null)
                {
                    if (existing.ListId == listId
                        && string.Equals(existing.Title, normalizedTitle, StringComparison.Ordinal))
                    {
                        return existing;
                    }

                    throw new TaskTodoException(
                        TaskTodoErrorCode.Conflict,
                        "The create request ID is already associated with a different task.");
                }

                EnsureListExists(connection, transaction, listId);
                var sortOrder = GetNextTaskSortOrder(connection, transaction, listId);
                var normalizedUtcNow = utcNow.ToUniversalTime();

                using var command = CreateCommand(connection, transaction, """
                    INSERT INTO TodoTasks (
                        Id, ListId, Title, IsCompleted, SortOrder,
                        CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, DeletedAtUtc)
                    VALUES (
                        $id, $listId, $title, 0, $sortOrder,
                        $createdAtUtc, $updatedAtUtc, NULL, NULL);
                    """);
                command.Parameters.AddWithValue("$id", requestId.ToString("D"));
                command.Parameters.AddWithValue("$listId", listId.ToString("D"));
                command.Parameters.AddWithValue("$title", normalizedTitle);
                command.Parameters.AddWithValue("$sortOrder", sortOrder);
                command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(normalizedUtcNow));
                command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(normalizedUtcNow));
                command.ExecuteNonQuery();

                return new TodoTask(
                    requestId,
                    listId,
                    normalizedTitle,
                    IsCompleted: false,
                    sortOrder,
                    normalizedUtcNow,
                    normalizedUtcNow,
                    CompletedAtUtc: null,
                    DeletedAtUtc: null);
            }),
            cancellationToken);

    public Task<TodoTask> UpdateTaskTitleAsync(
        Guid taskId,
        string normalizedTitle,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "update a task title",
            connection => InTransaction(connection, transaction =>
            {
                var existing = RequireActiveTask(connection, transaction, taskId);
                if (string.Equals(existing.Title, normalizedTitle, StringComparison.Ordinal))
                {
                    return existing;
                }

                var normalizedUtcNow = utcNow.ToUniversalTime();
                using var command = CreateCommand(connection, transaction, """
                    UPDATE TodoTasks
                    SET Title = $title, UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $id AND DeletedAtUtc IS NULL;
                    """);
                command.Parameters.AddWithValue("$title", normalizedTitle);
                command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(normalizedUtcNow));
                command.Parameters.AddWithValue("$id", taskId.ToString("D"));
                EnsureSingleRow(command.ExecuteNonQuery(), "Task title update lost its target.");
                return existing with { Title = normalizedTitle, UpdatedAtUtc = normalizedUtcNow };
            }),
            cancellationToken);

    public Task<TodoTask> SetTaskCompletedAsync(
        Guid taskId,
        bool isCompleted,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "set task completion",
            connection => InTransaction(connection, transaction =>
            {
                var existing = RequireActiveTask(connection, transaction, taskId);
                if (existing.IsCompleted == isCompleted)
                {
                    return existing;
                }

                var normalizedUtcNow = utcNow.ToUniversalTime();
                var completedAtUtc = isCompleted ? normalizedUtcNow : (DateTimeOffset?)null;
                using var command = CreateCommand(connection, transaction, """
                    UPDATE TodoTasks
                    SET IsCompleted = $isCompleted,
                        CompletedAtUtc = $completedAtUtc,
                        UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $id AND DeletedAtUtc IS NULL;
                    """);
                command.Parameters.AddWithValue("$isCompleted", isCompleted ? 1 : 0);
                command.Parameters.AddWithValue(
                    "$completedAtUtc",
                    completedAtUtc is null ? DBNull.Value : FormatUtc(completedAtUtc.Value));
                command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(normalizedUtcNow));
                command.Parameters.AddWithValue("$id", taskId.ToString("D"));
                EnsureSingleRow(command.ExecuteNonQuery(), "Task completion update lost its target.");

                return existing with
                {
                    IsCompleted = isCompleted,
                    CompletedAtUtc = completedAtUtc,
                    UpdatedAtUtc = normalizedUtcNow,
                };
            }),
            cancellationToken);

    public Task<TodoTask> SoftDeleteTaskAsync(
        Guid taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "soft-delete a task",
            connection => InTransaction(connection, transaction =>
            {
                var existing = ReadTaskById(connection, transaction, taskId, includeDeleted: true)
                    ?? throw NotFound("Task", taskId);
                if (existing.DeletedAtUtc is not null)
                {
                    return existing;
                }

                var normalizedUtcNow = utcNow.ToUniversalTime();
                using var command = CreateCommand(connection, transaction, """
                    UPDATE TodoTasks
                    SET DeletedAtUtc = $deletedAtUtc, UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $id AND DeletedAtUtc IS NULL;
                    """);
                command.Parameters.AddWithValue("$deletedAtUtc", FormatUtc(normalizedUtcNow));
                command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(normalizedUtcNow));
                command.Parameters.AddWithValue("$id", taskId.ToString("D"));
                EnsureSingleRow(command.ExecuteNonQuery(), "Task deletion lost its target.");
                return existing with { DeletedAtUtc = normalizedUtcNow, UpdatedAtUtc = normalizedUtcNow };
            }),
            cancellationToken);

    public Task<TodoTask> RestoreTaskAsync(
        Guid taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "restore a task",
            connection => InTransaction(connection, transaction =>
            {
                var existing = ReadTaskById(connection, transaction, taskId, includeDeleted: true)
                    ?? throw NotFound("Task", taskId);
                if (existing.DeletedAtUtc is null)
                {
                    return existing;
                }

                var normalizedUtcNow = utcNow.ToUniversalTime();
                using var command = CreateCommand(connection, transaction, """
                    UPDATE TodoTasks
                    SET DeletedAtUtc = NULL, UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $id AND DeletedAtUtc IS NOT NULL;
                    """);
                command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(normalizedUtcNow));
                command.Parameters.AddWithValue("$id", taskId.ToString("D"));
                EnsureSingleRow(command.ExecuteNonQuery(), "Task restore lost its target.");
                return existing with { DeletedAtUtc = null, UpdatedAtUtc = normalizedUtcNow };
            }),
            cancellationToken);

    public Task ReorderActiveTasksAsync(
        Guid listId,
        IReadOnlyList<Guid> orderedTaskIds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "reorder active tasks",
            connection => InTransaction(connection, transaction =>
            {
                EnsureListExists(connection, transaction, listId);
                var existingOrder = ReadActiveTaskIds(connection, transaction, listId);
                if (existingOrder.Count != orderedTaskIds.Count
                    || !existingOrder.ToHashSet().SetEquals(orderedTaskIds))
                {
                    throw new TaskTodoException(
                        TaskTodoErrorCode.Validation,
                        "Task order must contain the complete set of active, incomplete tasks for the list.");
                }

                if (existingOrder.SequenceEqual(orderedTaskIds))
                {
                    return true;
                }

                var normalizedUtcNow = utcNow.ToUniversalTime();
                for (var index = 0; index < orderedTaskIds.Count; index++)
                {
                    using var command = CreateCommand(connection, transaction, """
                        UPDATE TodoTasks
                        SET SortOrder = $sortOrder, UpdatedAtUtc = $updatedAtUtc
                        WHERE Id = $id
                          AND ListId = $listId
                          AND IsCompleted = 0
                          AND DeletedAtUtc IS NULL;
                        """);
                    command.Parameters.AddWithValue("$sortOrder", index);
                    command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(normalizedUtcNow));
                    command.Parameters.AddWithValue("$id", orderedTaskIds[index].ToString("D"));
                    command.Parameters.AddWithValue("$listId", listId.ToString("D"));
                    EnsureSingleRow(command.ExecuteNonQuery(), "Task reorder lost one of its targets.");
                }

                return true;
            }),
            cancellationToken);

    public Task<TaskList> CreateListAsync(
        Guid requestId,
        string normalizedName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "create a task list",
            connection => InTransaction(connection, transaction =>
            {
                var existing = ReadListById(connection, transaction, requestId);
                if (existing is not null)
                {
                    if (string.Equals(existing.Name, normalizedName, StringComparison.Ordinal))
                    {
                        return existing;
                    }

                    throw new TaskTodoException(
                        TaskTodoErrorCode.Conflict,
                        "The create request ID is already associated with a different list.");
                }

                var sortOrder = GetNextListSortOrder(connection, transaction);
                var normalizedUtcNow = utcNow.ToUniversalTime();
                using var command = CreateCommand(connection, transaction, """
                    INSERT INTO TaskLists (Id, Name, SortOrder, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ($id, $name, $sortOrder, $createdAtUtc, $updatedAtUtc);
                    """);
                command.Parameters.AddWithValue("$id", requestId.ToString("D"));
                command.Parameters.AddWithValue("$name", normalizedName);
                command.Parameters.AddWithValue("$sortOrder", sortOrder);
                command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(normalizedUtcNow));
                command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(normalizedUtcNow));
                command.ExecuteNonQuery();

                return new TaskList(requestId, normalizedName, sortOrder, normalizedUtcNow, normalizedUtcNow);
            }),
            cancellationToken);

    public Task<TaskList> RenameListAsync(
        Guid listId,
        string normalizedName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "rename a task list",
            connection => InTransaction(connection, transaction =>
            {
                var existing = ReadListById(connection, transaction, listId)
                    ?? throw NotFound("Task list", listId);
                if (string.Equals(existing.Name, normalizedName, StringComparison.Ordinal))
                {
                    return existing;
                }

                var normalizedUtcNow = utcNow.ToUniversalTime();
                using var command = CreateCommand(connection, transaction, """
                    UPDATE TaskLists
                    SET Name = $name, UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $id;
                    """);
                command.Parameters.AddWithValue("$name", normalizedName);
                command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(normalizedUtcNow));
                command.Parameters.AddWithValue("$id", listId.ToString("D"));
                EnsureSingleRow(command.ExecuteNonQuery(), "Task list rename lost its target.");
                return existing with { Name = normalizedName, UpdatedAtUtc = normalizedUtcNow };
            }),
            cancellationToken);

    public Task<TaskTodoSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => Enqueue(
            "read the application snapshot",
            connection =>
            {
                var lists = new List<TaskList>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        SELECT Id, Name, SortOrder, CreatedAtUtc, UpdatedAtUtc
                        FROM TaskLists
                        ORDER BY SortOrder, Id;
                        """;
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        lists.Add(ReadTaskList(reader));
                    }
                }

                var tasks = new List<TodoTask>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        SELECT Id, ListId, Title, IsCompleted, SortOrder,
                               CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, DeletedAtUtc
                        FROM TodoTasks
                        WHERE DeletedAtUtc IS NULL
                        ORDER BY ListId, IsCompleted, SortOrder, Id;
                        """;
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        tasks.Add(ReadTodoTask(reader));
                    }
                }

                return new TaskTodoSnapshot(lists.ToArray(), tasks.ToArray(), ReadSettings(connection));
            },
            cancellationToken);

    public Task SaveSettingsAsync(
        SettingsSnapshot settings,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "save application settings",
            connection => InTransaction(connection, transaction =>
            {
                WriteSettings(connection, transaction, settings);
                return true;
            }),
            cancellationToken);

    public Task<string> CreateBackupAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue(
            "create a database backup",
            connection => CreateRequiredBackup(connection, utcNow, "manual"),
            cancellationToken);

    public Task FlushPendingWritesAsync(CancellationToken cancellationToken = default)
        => _worker.FlushAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _worker.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _worker.DisposeAsync();

    private Task<T> Enqueue<T>(
        string operation,
        Func<SqliteConnection, T> action,
        CancellationToken cancellationToken)
        => _worker.Enqueue(
            connection => StorageExceptionMapper.Execute(operation, () => action(connection)),
            cancellationToken);

    private static T InTransaction<T>(
        SqliteConnection connection,
        Func<SqliteTransaction, T> action)
    {
        using var transaction = connection.BeginTransaction();
        var result = action(transaction);
        transaction.Commit();
        return result;
    }

    private static TodoTask RequireActiveTask(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid taskId)
        => ReadTaskById(connection, transaction, taskId, includeDeleted: false)
           ?? throw NotFound("Task", taskId);

    private static TodoTask? ReadTaskById(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid taskId,
        bool includeDeleted)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            $"""
                SELECT Id, ListId, Title, IsCompleted, SortOrder,
                       CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, DeletedAtUtc
                FROM TodoTasks
                WHERE Id = $id{(includeDeleted ? string.Empty : " AND DeletedAtUtc IS NULL")};
                """);
        command.Parameters.AddWithValue("$id", taskId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTodoTask(reader) : null;
    }

    private static TaskList? ReadListById(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid listId)
    {
        using var command = CreateCommand(connection, transaction, """
            SELECT Id, Name, SortOrder, CreatedAtUtc, UpdatedAtUtc
            FROM TaskLists
            WHERE Id = $id;
            """);
        command.Parameters.AddWithValue("$id", listId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTaskList(reader) : null;
    }

    private static List<Guid> ReadActiveTaskIds(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid listId)
    {
        using var command = CreateCommand(connection, transaction, """
            SELECT Id
            FROM TodoTasks
            WHERE ListId = $listId
              AND IsCompleted = 0
              AND DeletedAtUtc IS NULL
            ORDER BY SortOrder, Id;
            """);
        command.Parameters.AddWithValue("$listId", listId.ToString("D"));
        using var reader = command.ExecuteReader();
        var ids = new List<Guid>();
        while (reader.Read())
        {
            ids.Add(Guid.Parse(reader.GetString(0)));
        }

        return ids;
    }

    private static void EnsureListExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid listId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM TaskLists WHERE Id = $id);");
        command.Parameters.AddWithValue("$id", listId.ToString("D"));
        var exists = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
        if (!exists)
        {
            throw NotFound("Task list", listId);
        }
    }

    private static long GetNextTaskSortOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid listId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM TodoTasks WHERE ListId = $listId;");
        command.Parameters.AddWithValue("$listId", listId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long GetNextListSortOrder(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM TaskLists;");
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    internal static void WriteSettings(SqliteConnection connection, SqliteTransaction transaction, SettingsSnapshot settings)
    {
        using var command = CreateCommand(connection, transaction, """
            INSERT INTO Settings (Key, JsonValue)
            VALUES ($key, $jsonValue)
            ON CONFLICT(Key) DO UPDATE SET JsonValue = excluded.JsonValue;
            """);
        command.Parameters.AddWithValue("$key", ApplicationSettingsKey);
        command.Parameters.AddWithValue("$jsonValue", JsonSerializer.Serialize(settings, JsonOptions));
        command.ExecuteNonQuery();
    }

    internal static SettingsSnapshot ReadSettings(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT JsonValue FROM Settings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", ApplicationSettingsKey);
        var storedValue = command.ExecuteScalar();
        if (storedValue is null or DBNull)
        {
            return SettingsSnapshot.Default;
        }

        var settings = JsonSerializer.Deserialize<SettingsSnapshot>((string)storedValue, JsonOptions)
                       ?? throw new TaskTodoException(
                           TaskTodoErrorCode.InvalidStoredData,
                           "Stored application settings could not be read.");
        try
        {
            SettingsRules.Validate(settings);
            return settings;
        }
        catch (TaskTodoException exception)
        {
            throw new TaskTodoException(
                TaskTodoErrorCode.InvalidStoredData,
                "Stored application settings failed validation.",
                innerException: exception);
        }
    }

    private static TodoTask ReadTodoTask(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt64(3) != 0,
            reader.GetInt64(4),
            ParseUtc(reader.GetString(5)),
            ParseUtc(reader.GetString(6)),
            reader.IsDBNull(7) ? null : ParseUtc(reader.GetString(7)),
            reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8)));

    private static TaskList ReadTaskList(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt64(2),
            ParseUtc(reader.GetString(3)),
            ParseUtc(reader.GetString(4)));

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static void EnsureSingleRow(int affectedRows, string message)
    {
        if (affectedRows != 1)
        {
            throw new TaskTodoException(TaskTodoErrorCode.Conflict, message);
        }
    }

    private static TaskTodoException NotFound(string entityName, Guid id)
        => new(TaskTodoErrorCode.NotFound, $"{entityName} '{id:D}' was not found.");

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value)
        => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None)
            .ToUniversalTime();
}
