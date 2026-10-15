using CommunityToolkit.Mvvm.ComponentModel;
using TaskTodo.Core.Models;

namespace TaskTodo.App.ViewModels;

public sealed class TaskListViewModel(TaskList list, int pendingCount) : ObservableObject
{
    private string _name = list.Name;
    private int _pendingCount = pendingCount;
    private bool _isSelected;
    public Guid Id { get; } = list.Id;
    public string Name { get => _name; private set => SetProperty(ref _name, value); }
    public int PendingCount { get => _pendingCount; private set => SetProperty(ref _pendingCount, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public void Apply(TaskList value, int count)
    {
        Name = value.Name;
        PendingCount = count;
    }
}
