using System.Text.Json;
using Microsoft.Data.Sqlite;
using TaskTodo.Core.Services;

namespace TaskTodo.Infrastructure.Storage;

internal static class StorageExceptionMapper
{
    public static T Execute<T>(string operation, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            throw Map(operation, exception);
        }
    }

    public static void Execute(string operation, Action action)
        => Execute<object?>(operation, () =>
        {
            action();
            return null;
        });

    public static TaskTodoException Map(string operation, Exception exception)
    {
        if (exception is TaskTodoException taskTodoException)
        {
            return taskTodoException;
        }

        if (exception is UnauthorizedAccessException)
        {
            return new TaskTodoException(
                TaskTodoErrorCode.StorageAccessDenied,
                $"Storage access was denied while attempting to {operation}.",
                innerException: exception);
        }

        if (exception is JsonException or FormatException)
        {
            return new TaskTodoException(
                TaskTodoErrorCode.InvalidStoredData,
                $"Stored data is invalid while attempting to {operation}.",
                innerException: exception);
        }

        if (exception is SqliteException sqliteException)
        {
            return sqliteException.SqliteErrorCode switch
            {
                11 or 26 => new TaskTodoException(
                    TaskTodoErrorCode.DatabaseCorrupt,
                    $"The database is corrupt or is not a SQLite database; {operation} was not performed.",
                    innerException: exception),
                3 or 8 or 14 or 23 => new TaskTodoException(
                    TaskTodoErrorCode.StorageAccessDenied,
                    $"The database cannot be opened for writing; {operation} was not performed.",
                    innerException: exception),
                5 or 6 => new TaskTodoException(
                    TaskTodoErrorCode.StorageUnavailable,
                    $"The database is busy or locked while attempting to {operation}.",
                    isRetryable: true,
                    innerException: exception),
                19 => new TaskTodoException(
                    TaskTodoErrorCode.Conflict,
                    $"A database constraint rejected the attempt to {operation}.",
                    innerException: exception),
                _ => new TaskTodoException(
                    TaskTodoErrorCode.StorageUnavailable,
                    $"SQLite failed while attempting to {operation}.",
                    isRetryable: true,
                    innerException: exception),
            };
        }

        if (exception is IOException)
        {
            return new TaskTodoException(
                TaskTodoErrorCode.StorageUnavailable,
                $"A storage I/O error occurred while attempting to {operation}.",
                isRetryable: true,
                innerException: exception);
        }

        return new TaskTodoException(
            TaskTodoErrorCode.StorageUnavailable,
            $"Storage failed while attempting to {operation}.",
            isRetryable: true,
            innerException: exception);
    }
}
