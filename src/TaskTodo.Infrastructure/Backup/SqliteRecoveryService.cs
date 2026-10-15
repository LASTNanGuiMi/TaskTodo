using System.Globalization;
using Microsoft.Data.Sqlite;
using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Migrations;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.Infrastructure.Backup;

/// <summary>Recovery independent of opening the damaged repository. The target repository must be stopped.</summary>
public static class SqliteRecoveryService
{
    public static Task<TaskTodoImportPreview> ReadBackupAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run(() => StorageExceptionMapper.Execute("validate a SQLite backup", () =>
        {
            using var connection = OpenBackup(path);
            return ReadValidatedArchive(connection);
        }), cancellationToken);

    /// <returns>The directory containing the original database and sidecars, or null for a missing database.</returns>
    public static Task<string?> RestoreAsync(
        TaskTodoStorageOptions options, string backupPath, CancellationToken cancellationToken = default)
        => Task.Run(() => StorageExceptionMapper.Execute("restore a SQLite backup", () => Restore(options, backupPath)),
            cancellationToken);

    private static string? Restore(TaskTodoStorageOptions options, string backupPath)
    {
        if (string.Equals(Path.GetFullPath(backupPath), options.DatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new TaskTodoException(TaskTodoErrorCode.Validation, "The recovery source must be a separate backup file.");
        }

        var directory = Path.GetDirectoryName(options.DatabasePath)!;
        Directory.CreateDirectory(directory);
        var stagedPath = Path.Combine(directory, $".restore-{Guid.NewGuid():N}.db");
        try
        {
            using (var source = OpenBackup(backupPath))
            {
                var preview = ReadValidatedArchive(source);
                using var staged = new SqliteConnection(ConnectionString(stagedPath, SqliteOpenMode.ReadWriteCreate));
                staged.Open();
                source.BackupDatabase(staged);
                // Recovery can originate on a different monitor layout; startup computes a visible placement.
                var settings = SqliteTaskTodoRepository.ReadSettings(staged) with
                {
                    WindowPlacement = null,
                    DockSide = SettingsSnapshot.Default.DockSide,
                    DockOffset = SettingsSnapshot.Default.DockOffset,
                    LastPresentationState = WindowPresentationState.Expanded,
                    SelectedListId = preview.Archive.Lists[0].Id,
                };
                using (var transaction = staged.BeginTransaction())
                {
                    SqliteTaskTodoRepository.WriteSettings(staged, transaction, settings);
                    transaction.Commit();
                }

                using var command = staged.CreateCommand();
                command.CommandText = "PRAGMA journal_mode = DELETE;";
                command.ExecuteNonQuery();
                ReadValidatedArchive(staged);
            }

            if (!File.Exists(options.DatabasePath))
            {
                File.Move(stagedPath, options.DatabasePath);
                return null;
            }

            var preservedDirectory = Path.Combine(directory,
                $"RecoveryOriginal-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(preservedDirectory);
            var movedSidecars = new List<(string Original, string Preserved)>();
            try
            {
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var original = options.DatabasePath + suffix;
                    if (File.Exists(original))
                    {
                        var preserved = Path.Combine(preservedDirectory, Path.GetFileName(original));
                        File.Move(original, preserved);
                        movedSidecars.Add((original, preserved));
                    }
                }

                File.Replace(stagedPath, options.DatabasePath,
                    Path.Combine(preservedDirectory, Path.GetFileName(options.DatabasePath)));
            }
            catch
            {
                foreach (var (original, preserved) in movedSidecars)
                {
                    File.Move(preserved, original);
                }

                throw;
            }

            return preservedDirectory;
        }
        finally
        {
            File.Delete(stagedPath);
        }
    }

    private static SqliteConnection OpenBackup(string path)
    {
        var connection = new SqliteConnection(ConnectionString(Path.GetFullPath(path), SqliteOpenMode.ReadOnly));
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static TaskTodoImportPreview ReadValidatedArchive(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        if (!string.Equals(Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.Ordinal))
        {
            throw new TaskTodoException(TaskTodoErrorCode.DatabaseCorrupt, "The selected backup failed its integrity check.");
        }

        command.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != DatabaseSchema.CurrentVersion)
        {
            throw new TaskTodoException(TaskTodoErrorCode.UnsupportedDatabaseVersion, "The selected backup has an unsupported schema version.");
        }

        var archive = SqliteTaskTodoRepository.ReadArchive(connection, DateTimeOffset.UtcNow);
        return new TaskTodoImportPreview(ArchiveRules.ValidateAndCopy(archive));
    }

    private static string ConnectionString(string path, SqliteOpenMode mode)
        => new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
}
