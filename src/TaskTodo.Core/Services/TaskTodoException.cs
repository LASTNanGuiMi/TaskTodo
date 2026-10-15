namespace TaskTodo.Core.Services;

public sealed class TaskTodoException : Exception
{
    public TaskTodoException(
        TaskTodoErrorCode code,
        string message,
        bool isRetryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        IsRetryable = isRetryable;
    }

    public TaskTodoErrorCode Code { get; }

    public bool IsRetryable { get; }
}
