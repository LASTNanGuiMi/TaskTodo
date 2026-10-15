using System.Globalization;
using Microsoft.Data.Sqlite;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.Infrastructure.Backup;

public sealed class SqliteBackupService : ISqliteBackupService
{
    public string CreateBackup(
        SqliteConnection sourceConnection,
        TaskTodoStorageOptions options,
        DateTimeOffset utcNow,
        string purpose)
    {
        ArgumentNullException.ThrowIfNull(sourceConnection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        Directory.CreateDirectory(options.BackupDirectory);
        var databaseName = Path.GetFileNameWithoutExtension(options.DatabasePath);
        var timestamp = utcNow.ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        var safePurpose = new string(purpose.Where(character => char.IsLetterOrDigit(character) || character == '-').ToArray());
        var finalPath = Path.Combine(
            options.BackupDirectory,
            $"{databaseName}.{timestamp}.{safePurpose}.{Guid.NewGuid():N}.db");
        var temporaryPath = finalPath + ".tmp";

        try
        {
            var destinationConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString();

            using (var destination = new SqliteConnection(destinationConnectionString))
            {
                destination.Open();
                sourceConnection.BackupDatabase(destination);

                using var integrityCommand = destination.CreateCommand();
                integrityCommand.CommandText = "PRAGMA quick_check;";
                var result = Convert.ToString(integrityCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SQLite backup did not pass quick_check.");
                }
            }

            File.Move(temporaryPath, finalPath);
            RotateBackups(options, databaseName, finalPath);
            return finalPath;
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void RotateBackups(
        TaskTodoStorageOptions options,
        string databaseName,
        string currentBackupPath)
    {
        var currentFullPath = Path.GetFullPath(currentBackupPath);
        var backups = new DirectoryInfo(options.BackupDirectory)
            .EnumerateFiles($"{databaseName}.*.db", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(
                Path.GetFullPath(file.FullName),
                currentFullPath,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(options.MaximumBackupCount - 1)
            .ToArray();

        foreach (var backup in backups)
        {
            backup.Delete();
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch
        {
            // Preserve the original backup failure. A stale .tmp file is never treated as a valid backup.
        }
    }
}
