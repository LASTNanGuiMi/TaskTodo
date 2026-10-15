using Microsoft.Data.Sqlite;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;

namespace TaskTodo.Infrastructure.Storage;

public sealed partial class SqliteTaskTodoRepository
{
    public Task<IReadOnlyList<TodoTask>> GetDeletedTasksAsync(CancellationToken cancellationToken = default)
        => Enqueue<IReadOnlyList<TodoTask>>(
            "read recently deleted tasks",
            connection => ReadTasks(connection, "WHERE DeletedAtUtc IS NOT NULL ORDER BY DeletedAtUtc DESC, Id"),
            cancellationToken);

    public Task<TaskTodoArchive> GetArchiveAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        => Enqueue("read the export archive", connection => ReadArchive(connection, utcNow), cancellationToken);

    public Task<string> ReplaceDataAsync(
        TaskTodoArchive archive,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => Enqueue("replace application data", connection =>
        {
            var settings = ReadSettings(connection);
            var backupPath = CreateRequiredBackup(connection, utcNow, "before-import");
            return InTransaction(connection, transaction =>
            {
                using (var clear = CreateCommand(connection, transaction, "DELETE FROM TodoTasks; DELETE FROM TaskLists;"))
                {
                    clear.ExecuteNonQuery();
                }

                foreach (var list in archive.Lists)
                {
                    using var insert = CreateCommand(connection, transaction, """
                        INSERT INTO TaskLists (Id, Name, SortOrder, CreatedAtUtc, UpdatedAtUtc)
                        VALUES ($id, $name, $order, $created, $updated);
                        """);
                    insert.Parameters.AddWithValue("$id", list.Id.ToString("D"));
                    insert.Parameters.AddWithValue("$name", list.Name);
                    insert.Parameters.AddWithValue("$order", list.SortOrder);
                    insert.Parameters.AddWithValue("$created", FormatUtc(list.CreatedAtUtc));
                    insert.Parameters.AddWithValue("$updated", FormatUtc(list.UpdatedAtUtc));
                    insert.ExecuteNonQuery();
                }

                foreach (var task in archive.Tasks)
                {
                    using var insert = CreateCommand(connection, transaction, """
                        INSERT INTO TodoTasks (Id, ListId, Title, IsCompleted, SortOrder,
                            CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, DeletedAtUtc)
                        VALUES ($id, $list, $title, $completed, $order, $created, $updated, $completedAt, $deletedAt);
                        """);
                    insert.Parameters.AddWithValue("$id", task.Id.ToString("D"));
                    insert.Parameters.AddWithValue("$list", task.ListId.ToString("D"));
                    insert.Parameters.AddWithValue("$title", task.Title);
                    insert.Parameters.AddWithValue("$completed", task.IsCompleted ? 1 : 0);
                    insert.Parameters.AddWithValue("$order", task.SortOrder);
                    insert.Parameters.AddWithValue("$created", FormatUtc(task.CreatedAtUtc));
                    insert.Parameters.AddWithValue("$updated", FormatUtc(task.UpdatedAtUtc));
                    insert.Parameters.AddWithValue("$completedAt", task.CompletedAtUtc is { } completed ? FormatUtc(completed) : DBNull.Value);
                    insert.Parameters.AddWithValue("$deletedAt", task.DeletedAtUtc is { } deleted ? FormatUtc(deleted) : DBNull.Value);
                    insert.ExecuteNonQuery();
                }

                var importedSettings = settings with
                {
                    BackgroundOpacity = archive.Settings.BackgroundOpacity,
                    IsAlwaysOnTop = archive.Settings.IsAlwaysOnTop,
                    Theme = archive.Settings.Theme,
                    SelectedListId = archive.Lists.Any(list => list.Id == settings.SelectedListId)
                        ? settings.SelectedListId : archive.Lists[0].Id,
                };
                WriteSettings(connection, transaction, importedSettings);
                return backupPath;
            });
        }, cancellationToken);

    internal static TaskTodoArchive ReadArchive(SqliteConnection connection, DateTimeOffset utcNow)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var lists = new List<TaskList>();
        using (var command = CreateCommand(connection, transaction, """
            SELECT Id, Name, SortOrder, CreatedAtUtc, UpdatedAtUtc FROM TaskLists ORDER BY SortOrder, Id;
            """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                lists.Add(ReadTaskList(reader));
            }
        }

        var tasks = ReadTasks(connection, "ORDER BY ListId, IsCompleted, SortOrder, Id", transaction);
        var settings = ReadSettings(connection, transaction);
        transaction.Commit();
        return new TaskTodoArchive(TaskTodoArchive.CurrentSchemaVersion, utcNow,
            lists.AsReadOnly(), tasks,
            new PortableSettings(settings.BackgroundOpacity, settings.IsAlwaysOnTop, settings.Theme));
    }

    private static IReadOnlyList<TodoTask> ReadTasks(
        SqliteConnection connection, string clause, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT Id, ListId, Title, IsCompleted, SortOrder,
                CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, DeletedAtUtc FROM TodoTasks {clause};
            """;
        using var reader = command.ExecuteReader();
        var tasks = new List<TodoTask>();
        while (reader.Read())
        {
            tasks.Add(ReadTodoTask(reader));
        }

        return tasks.AsReadOnly();
    }

    private string CreateRequiredBackup(SqliteConnection connection, DateTimeOffset utcNow, string purpose)
    {
        try
        {
            return _backupService.CreateBackup(connection, _options, utcNow, purpose);
        }
        catch (Exception exception)
        {
            throw new TaskTodoException(TaskTodoErrorCode.BackupFailed,
                "The required backup failed; application data was not replaced.", innerException: exception);
        }
    }
}
