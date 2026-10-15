using CommunityToolkit.Mvvm.ComponentModel;
using TaskTodo.Core.Models;

namespace TaskTodo.App.ViewModels;

public sealed class TaskRowViewModel(TodoTask task) : ObservableObject
{
    private TodoTask _task = task;
    private bool _isEditing;
    private string _editTitle = task.Title;

    public Guid Id => _task.Id;
    public string Title => _task.Title;
    public bool IsCompleted => _task.IsCompleted;
    public long SortOrder => _task.SortOrder;

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string EditTitle
    {
        get => _editTitle;
        set => SetProperty(ref _editTitle, value);
    }

    public void Apply(TodoTask value)
    {
        if (_task == value) return;
        _task = value;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(SortOrder));
    }
}
