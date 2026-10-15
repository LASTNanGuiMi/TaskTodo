namespace TaskTodo.Core.Services;

public enum TaskTodoErrorCode
{
    Validation,
    NotFound,
    Conflict,
    UnsupportedDatabaseVersion,
    DatabaseCorrupt,
    StorageAccessDenied,
    StorageUnavailable,
    BackupFailed,
    InvalidStoredData,
    StorageStopping,
}
