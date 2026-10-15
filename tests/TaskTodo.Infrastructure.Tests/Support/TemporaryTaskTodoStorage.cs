using Microsoft.Data.Sqlite;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Backup;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.Infrastructure.Tests.Support;

internal sealed class TemporaryTaskTodoStorage : IDisposable
{
    private static readonly string TestRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "TaskTodo.Tests"));
    private bool _disposed;

    public TemporaryTaskTodoStorage()
    {
        RootPath = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        Options = new TaskTodoStorageOptions(
            Path.Combine(RootPath, "tasktodo.db"),
            Path.Combine(RootPath, "Backups"));
    }

    public string RootPath { get; }

    public TaskTodoStorageOptions Options { get; }

    public async Task<TaskTodoService> OpenServiceAsync(
        TimeProvider? timeProvider = null,
        ISqliteBackupService? backupService = null)
    {
        var repository = await SqliteTaskTodoRepository.OpenAsync(
            Options,
            timeProvider,
            backupService);
        return new TaskTodoService(repository, timeProvider);
    }

    public SqliteConnection OpenConnection(SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Options.DatabasePath,
                Mode = mode,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var resolvedRoot = Path.GetFullPath(RootPath);
        var expectedPrefix = TestRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolvedRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete a path outside the TaskTodo test root.");
        }

        if (Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }
}
