using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;

namespace TaskTodo.Core.Services;

public static class SettingsRules
{
    public static void Validate(SettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!double.IsFinite(settings.BackgroundOpacity)
            || settings.BackgroundOpacity is < 0.15 or > 1.0)
        {
            throw new TaskTodoException(
                TaskTodoErrorCode.Validation,
                "Background opacity must be between 0.15 and 1.0.");
        }

        if (!Enum.IsDefined(settings.Theme) || !Enum.IsDefined(settings.DockSide) || !Enum.IsDefined(settings.CollapseStyle)
            || !Enum.IsDefined(settings.Palette) || !Enum.IsDefined(settings.Material))
        {
            throw new TaskTodoException(TaskTodoErrorCode.Validation, "Settings contain an unsupported enum value.");
        }

        if (!double.IsFinite(settings.DockOffset) || settings.DockOffset is < 0 or > 1)
        {
            throw new TaskTodoException(TaskTodoErrorCode.Validation, "Dock offset must be between 0 and 1.");
        }

        if (settings.LastPresentationState is not (
            WindowPresentationState.Expanded or WindowPresentationState.Docked))
        {
            throw new TaskTodoException(
                TaskTodoErrorCode.Validation,
                "Only Expanded or Docked can be persisted as a startup presentation state.");
        }

        if (settings.WindowPlacement is { } placement)
        {
            if (!placement.Bounds.IsFinite || placement.Bounds.Width <= 0 || placement.Bounds.Height <= 0)
            {
                throw new TaskTodoException(TaskTodoErrorCode.Validation, "Window placement is invalid.");
            }

            if (placement.MonitorId?.Length > 512)
            {
                throw new TaskTodoException(TaskTodoErrorCode.Validation, "Monitor identifier is too long.");
            }
        }
    }
}
