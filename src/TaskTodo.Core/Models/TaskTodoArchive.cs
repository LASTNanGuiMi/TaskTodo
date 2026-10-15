namespace TaskTodo.Core.Models;

public sealed record PortableSettings(double BackgroundOpacity, bool IsAlwaysOnTop, ThemePreference Theme);

public sealed record TaskTodoArchive(
    int SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<TaskList> Lists,
    IReadOnlyList<TodoTask> Tasks,
    PortableSettings Settings)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record TaskTodoImportPreview(TaskTodoArchive Archive)
{
    public int ListCount => Archive.Lists.Count;
    public int TaskCount => Archive.Tasks.Count;
    public int DeletedTaskCount => Archive.Tasks.Count(task => task.DeletedAtUtc is not null);
    public int CompletedTaskCount => Archive.Tasks.Count(task => task.IsCompleted && task.DeletedAtUtc is null);
}
