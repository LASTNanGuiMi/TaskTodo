using TaskTodo.Core.Models;

namespace TaskTodo.Core.Services;

/// <summary>Validation at the external data boundary; database writes receive one stable copy.</summary>
public static class ArchiveRules
{
    public static TaskTodoArchive ValidateAndCopy(TaskTodoArchive archive)
    {
        if (archive is null || archive.SchemaVersion != TaskTodoArchive.CurrentSchemaVersion)
        {
            throw Invalid("Unsupported or missing archive schemaVersion.");
        }

        if (archive.Lists is null || archive.Lists.Count == 0 || archive.Tasks is null || archive.Settings is null)
        {
            throw Invalid("An archive must contain lists, tasks and portable settings.");
        }

        ValidateUtc(archive.ExportedAtUtc);
        var lists = archive.Lists.ToArray();
        var tasks = archive.Tasks.ToArray();
        var listIds = new HashSet<Guid>();
        foreach (var list in lists)
        {
            if (list is null || list.Id == Guid.Empty || !listIds.Add(list.Id) || list.SortOrder < 0
                || list.SortOrder == long.MaxValue)
            {
                throw Invalid("A list has a missing or duplicate ID, or an invalid order.");
            }

            if (TaskTextRules.NormalizeListName(list.Name) != list.Name)
            {
                throw Invalid("List names must be normalized plain text.");
            }

            ValidateTimes(list.CreatedAtUtc, list.UpdatedAtUtc);
        }

        var taskIds = new HashSet<Guid>();
        foreach (var task in tasks)
        {
            if (task is null || task.Id == Guid.Empty || !taskIds.Add(task.Id) || !listIds.Contains(task.ListId)
                || task.SortOrder < 0 || task.SortOrder == long.MaxValue)
            {
                throw Invalid("A task has a missing or duplicate ID, an unknown list, or an invalid order.");
            }

            if (TaskTextRules.NormalizeTitle(task.Title) != task.Title)
            {
                throw Invalid("Task titles must be normalized plain text.");
            }

            ValidateTimes(task.CreatedAtUtc, task.UpdatedAtUtc);
            if (task.IsCompleted != task.CompletedAtUtc.HasValue)
            {
                throw Invalid("Task completion and completion time disagree.");
            }

            ValidateOptionalTime(task.CompletedAtUtc, task.CreatedAtUtc, task.UpdatedAtUtc);
            ValidateOptionalTime(task.DeletedAtUtc, task.CreatedAtUtc, task.UpdatedAtUtc);
        }

        SettingsRules.Validate(SettingsSnapshot.Default with
        {
            BackgroundOpacity = archive.Settings.BackgroundOpacity,
            IsAlwaysOnTop = archive.Settings.IsAlwaysOnTop,
            Theme = archive.Settings.Theme,
        });
        return archive with { Lists = Array.AsReadOnly(lists), Tasks = Array.AsReadOnly(tasks) };
    }

    private static void ValidateTimes(DateTimeOffset created, DateTimeOffset updated)
    {
        ValidateUtc(created);
        ValidateUtc(updated);
        if (updated < created)
        {
            throw Invalid("An update time precedes its creation time.");
        }
    }

    private static void ValidateOptionalTime(DateTimeOffset? value, DateTimeOffset created, DateTimeOffset updated)
    {
        if (value is { } timestamp)
        {
            ValidateUtc(timestamp);
            if (timestamp < created || timestamp > updated)
            {
                throw Invalid("A task state timestamp is outside its creation/update interval.");
            }
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            throw Invalid("Archive timestamps must be present and use UTC.");
        }
    }

    private static TaskTodoException Invalid(string message) => new(TaskTodoErrorCode.Validation, message);
}
