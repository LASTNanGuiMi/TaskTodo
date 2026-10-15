namespace TaskTodo.Core.Models;

public sealed record TaskTodoSnapshot(
    IReadOnlyList<TaskList> Lists,
    IReadOnlyList<TodoTask> Tasks,
    SettingsSnapshot Settings)
{
    public int GetIncompleteCount(Guid listId)
        => Tasks.Count(task => task.ListId == listId && !task.IsCompleted && task.DeletedAtUtc is null);
}
