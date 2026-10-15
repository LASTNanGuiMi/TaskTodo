namespace TaskTodo.Core.Models;

public sealed record TodoTask(
    Guid Id,
    Guid ListId,
    string Title,
    bool IsCompleted,
    long SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? DeletedAtUtc);
