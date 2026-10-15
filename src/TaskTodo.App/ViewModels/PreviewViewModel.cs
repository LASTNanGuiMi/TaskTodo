using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTodo.App.State;
using TaskTodo.Core.Services;
using TaskTodo.Core.Models;

namespace TaskTodo.App.ViewModels;

public sealed class PreviewViewModel : ObservableObject, IDisposable
{
    private readonly TaskTodoService _service;
    private readonly TaskStore _store;
    private Guid _renderedListId;
    private IReadOnlyList<TodoTask>? _renderedTasks;
    private IReadOnlyList<TaskList>? _renderedLists;
    private bool _isSaving;
    private string? _errorMessage;
    private Func<Task>? _retry;
    private Task<bool> _pendingOperation = Task.FromResult(true);

    public PreviewViewModel(TaskTodoService service, TaskStore store)
    {
        _service = service;
        _store = store;
        ListId = store.Snapshot.Lists[0].Id;
        RetryCommand = new AsyncRelayCommand(async () =>
        {
            if (_retry is { } retry) await RunAsync(retry);
        }, () => !IsSaving);
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = null);
        _store.PropertyChanged += StoreChanged;
        _store.DataReplaced += DataReplaced;
        Refresh();
    }

    public Guid ListId { get; private set; }
    public string ListName => _store.Snapshot.Lists.FirstOrDefault(list => list.Id == ListId)?.Name ?? "清单";
    public ObservableCollection<TaskRowViewModel> Rows { get; private set; } = [];
    public bool IsEmpty => Rows.Count == 0;
    public string PendingSummary => $"{Rows.Count(row => !row.IsCompleted)} 项待完成";
    public SurfaceMaterial Material => _store.Snapshot.Settings.Material;
    public double SurfaceOpacity => _store.Snapshot.Settings.GetSurfaceOpacity();
    public bool IsAlwaysOnTop => _store.Snapshot.Settings.IsAlwaysOnTop;
    public bool CanInteract => !IsSaving;
    public bool HasError => ErrorMessage is not null;
    public AsyncRelayCommand RetryCommand { get; }
    public RelayCommand DismissErrorCommand { get; }
    public Task<bool> PendingOperation => _pendingOperation;

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            SetProperty(ref _isSaving, value);
            OnPropertyChanged(nameof(CanInteract));
            RetryCommand.NotifyCanExecuteChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { SetProperty(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }

    public void SelectList(Guid id)
    {
        if (ListId == id) return;
        ListId = id;
        Refresh();
    }

    public Task<bool> SetCompletedAsync(TaskRowViewModel row, bool completed)
        => RunAsync(async () => _store.ApplyTask(await _service.SetCompletedAsync(row.Id, completed)));

    private Task<bool> RunAsync(Func<Task> operation)
    {
        if (IsSaving) return Task.FromResult(false);
        _pendingOperation = ExecuteAsync(operation);
        return _pendingOperation;
    }

    private async Task<bool> ExecuteAsync(Func<Task> operation)
    {
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            await operation();
            _retry = null;
            return true;
        }
        catch (TaskTodoException error)
        {
            _retry = operation;
            ErrorMessage = OperationError.Describe(error);
            return false;
        }
        finally { IsSaving = false; }
    }

    private void StoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(_renderedTasks, _store.Snapshot.Tasks) || !ReferenceEquals(_renderedLists, _store.Snapshot.Lists))
            Refresh();
        else
        {
            OnPropertyChanged(nameof(SurfaceOpacity));
            OnPropertyChanged(nameof(Material));
            OnPropertyChanged(nameof(IsAlwaysOnTop));
        }
    }

    private void DataReplaced(object? sender, EventArgs e)
    {
        _retry = null;
        ErrorMessage = null;
        _pendingOperation = Task.FromResult(true);
        if (!_store.Snapshot.Lists.Any(list => list.Id == ListId)) ListId = _store.Snapshot.Lists[0].Id;
        Refresh();
    }

    private void Refresh()
    {
        var existing = Rows.ToDictionary(row => row.Id);
        var tasks = _store.Snapshot.Tasks.Where(task => task.ListId == ListId && task.DeletedAtUtc is null)
            .OrderBy(task => task.IsCompleted).ThenBy(task => task.SortOrder).ThenBy(task => task.Id).ToArray();
        var rows = tasks.Select(task =>
        {
            var row = existing.GetValueOrDefault(task.Id) ?? new TaskRowViewModel(task);
            row.Apply(task);
            return row;
        }).ToArray();
        if (_renderedListId != ListId)
        {
            Rows = new ObservableCollection<TaskRowViewModel>(rows);
            OnPropertyChanged(nameof(Rows));
        }
        else
        {
            foreach (var removed in Rows.Except(rows).ToArray()) Rows.Remove(removed);
            for (var index = 0; index < rows.Length; index++)
            {
                var oldIndex = Rows.IndexOf(rows[index]);
                if (oldIndex < 0) Rows.Insert(index, rows[index]);
                else if (oldIndex != index) Rows.Move(oldIndex, index);
            }
        }
        _renderedListId = ListId;
        _renderedTasks = _store.Snapshot.Tasks;
        _renderedLists = _store.Snapshot.Lists;
        OnPropertyChanged(nameof(ListName));
        OnPropertyChanged(nameof(PendingSummary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SurfaceOpacity));
        OnPropertyChanged(nameof(Material));
        OnPropertyChanged(nameof(IsAlwaysOnTop));
    }

    public void Dispose()
    {
        _store.PropertyChanged -= StoreChanged;
        _store.DataReplaced -= DataReplaced;
    }
}
