using System.Text;

namespace TaskTodo.Core.Services;

public static class TaskTextRules
{
    public const int MaximumTitleLength = 500;
    public const int MaximumListNameLength = 200;

    public static string NormalizeTitle(string? title)
        => NormalizeRequiredText(title, MaximumTitleLength, "Task title");

    public static string NormalizeListName(string? name)
        => NormalizeRequiredText(name, MaximumListNameLength, "List name");

    private static string NormalizeRequiredText(string? value, int maximumLength, string fieldName)
    {
        if (value is null)
        {
            throw new TaskTodoException(TaskTodoErrorCode.Validation, $"{fieldName} is required.");
        }

        var builder = new StringBuilder(value.Length);
        var previousWasLineBreak = false;
        foreach (var character in value)
        {
            var isLineBreak = character is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';
            if (isLineBreak)
            {
                if (!previousWasLineBreak)
                {
                    builder.Append(' ');
                }

                previousWasLineBreak = true;
                continue;
            }

            previousWasLineBreak = false;
            builder.Append(character);
        }

        var normalized = builder.ToString().Trim();
        if (normalized.Length == 0)
        {
            throw new TaskTodoException(TaskTodoErrorCode.Validation, $"{fieldName} cannot be empty.");
        }

        if (normalized.Length > maximumLength)
        {
            throw new TaskTodoException(
                TaskTodoErrorCode.Validation,
                $"{fieldName} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
