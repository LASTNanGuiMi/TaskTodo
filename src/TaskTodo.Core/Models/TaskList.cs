namespace TaskTodo.Core.Models;

public sealed record TaskList(
    Guid Id,
    string Name,
    long SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
