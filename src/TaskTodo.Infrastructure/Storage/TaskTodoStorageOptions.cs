using Microsoft.Data.Sqlite;

namespace TaskTodo.Infrastructure.Storage;

public sealed record TaskTodoStorageOptions
{
    public TaskTodoStorageOptions(string databasePath, string backupDirectory, int maximumBackupCount = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        DatabasePath = Path.GetFullPath(databasePath);
        BackupDirectory = Path.GetFullPath(backupDirectory);
        if (maximumBackupCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBackupCount));
        }

        MaximumBackupCount = maximumBackupCount;
    }

    public string DatabasePath { get; }

    public string BackupDirectory { get; }

    public int MaximumBackupCount { get; }

    public static TaskTodoStorageOptions CreateDefault()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("LocalApplicationData is unavailable for the current user.");
        }

        var applicationDirectory = Path.Combine(localApplicationData, "TaskTodo");
        return new TaskTodoStorageOptions(
            Path.Combine(applicationDirectory, "tasktodo.db"),
            Path.Combine(applicationDirectory, "Backups"));
    }

    internal string BuildConnectionString()
        => new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
}
