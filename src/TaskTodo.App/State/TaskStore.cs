using CommunityToolkit.Mvvm.ComponentModel;
using TaskTodo.Core.Models;

namespace TaskTodo.App.State;

// Owned by the UI thread. Only committed service results enter this snapshot.
public sealed class TaskStore(TaskTodoSnapshot snapshot) : ObservableObject
{
    public event EventHandler? DataReplaced;

    public void ReplaceData(TaskTodoSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
        DataReplaced?.Invoke(this, EventArgs.Empty);
    }
    private TaskTodoSnapshot _snapshot = snapshot;

    public TaskTodoSnapshot Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public void ApplyTask(TodoTask task)
        => Snapshot = Snapshot with
        {
            Tasks = Snapshot.Tasks.Where(item => item.Id != task.Id)
                .Concat(task.DeletedAtUtc is null ? [task] : Array.Empty<TodoTask>())
                .OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToArray(),
        };

    public void ApplySnapshot(TaskTodoSnapshot snapshot) => Snapshot = snapshot;

    public void ApplyList(TaskList list)
        => Snapshot = Snapshot with
        {
            Lists = Snapshot.Lists.Where(item => item.Id != list.Id).Append(list)
                .OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToArray(),
        };

    public void ApplySettings(SettingsSnapshot settings)
        => Snapshot = Snapshot with { Settings = settings };
}
