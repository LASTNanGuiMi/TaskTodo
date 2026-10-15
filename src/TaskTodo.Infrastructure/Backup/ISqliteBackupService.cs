using Microsoft.Data.Sqlite;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.Infrastructure.Backup;

public interface ISqliteBackupService
{
    string CreateBackup(
        SqliteConnection sourceConnection,
        TaskTodoStorageOptions options,
        DateTimeOffset utcNow,
        string purpose);
}
