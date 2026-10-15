using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTodo.App.State;
using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;

namespace TaskTodo.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly TaskTodoService _service;
    private readonly TimeProvider _clock;
    private Guid _listId;
    private Guid _renderedListId;
    private IReadOnlyList<TodoTask>? _renderedTasks;
    private IReadOnlyList<TaskList>? _renderedLists;
    private SettingsSnapshot _settings;
    private Task<bool> _pendingOperation = Task.FromResult(true);
    private Func<Task>? _retry;
    private string _newTitle = "";
    private Guid _createRequestId = Guid.NewGuid();
    private bool _isSaving;
    private bool _isClosing;
    private bool _isSettingsOpen;
    private string? _errorMessage;
    private Guid? _undoId;
    private DateTimeOffset _undoDeadline;

    public MainWindowViewModel(TaskTodoService service, TaskStore store, TimeProvider? clock = null)
    {
        _service = service;
        Store = store;
        _clock = clock ?? TimeProvider.System;
        _settings = store.Snapshot.Settings;
        _listId = store.Snapshot.Lists.FirstOrDefault(list => list.Id == _settings.SelectedListId)?.Id
            ?? store.Snapshot.Lists[0].Id;
        AddCommand = new AsyncRelayCommand(async () => await AddAsync(), () => CanInteract);
        UndoCommand = new AsyncRelayCommand(async () => await UndoAsync(), () => CanInteract && CanUndo);
        RetryCommand = new AsyncRelayCommand(async () => await RetryAsync(), () => CanInteract && _retry is not null);
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = null);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        Store.PropertyChanged += StoreChanged;
        Store.DataReplaced += DataReplaced;
        RefreshRows();
    }

    public TaskStore Store { get; }
    public ObservableCollection<TaskListViewModel> Lists { get; } = [];
    public Guid SelectedListId => _listId;
    public ObservableCollection<TaskRowViewModel> ActiveTasks { get; private set; } = [];
    public ObservableCollection<TaskRowViewModel> CompletedTasks { get; private set; } = [];
    public TaskRowViewModel? EditingTask { get; private set; }
    public string ListName => Store.Snapshot.Lists.First(list => list.Id == _listId).Name;
    public int PendingCount => ActiveTasks.Count;
    public string PendingSummary => $"{PendingCount} 项待完成";
    public string CompletedSummary => $"已完成 · {CompletedTasks.Count}";
    public bool IsEmpty => ActiveTasks.Count == 0 && CompletedTasks.Count == 0;
    public double CompletionRatio => IsEmpty ? 0 : (double)CompletedTasks.Count / (ActiveTasks.Count + CompletedTasks.Count);
    public bool CanInteract => !IsSaving && !IsClosing;
    public bool IsInputReadOnly => IsSaving || IsClosing;
    public bool CanUndo => _undoId is not null && _clock.GetUtcNow() < _undoDeadline;
    public SettingsSnapshot Settings => _settings;
    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public RelayCommand DismissErrorCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public event EventHandler? SettingsChanged;

    public string NewTitle
    {
        get => _newTitle;
        set
        {
            if (SetProperty(ref _newTitle, value))
                _createRequestId = Guid.NewGuid();
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set { SetProperty(ref _isSaving, value); NotifyCommands(); }
    }

    public bool IsClosing
    {
        get => _isClosing;
        private set { SetProperty(ref _isClosing, value); NotifyCommands(); }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { SetProperty(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => ErrorMessage is not null;

    public double BackgroundOpacity
    {
        get => _settings.BackgroundOpacity;
        set => ChangeSettings(_settings with { BackgroundOpacity = value });
    }

    public bool IsAlwaysOnTop
    {
        get => _settings.IsAlwaysOnTop;
        set => ChangeSettings(_settings with { IsAlwaysOnTop = value });
    }

    public ThemePreference Theme
    {
        get => _settings.Theme;
        set => ChangeSettings(_settings with { Theme = value });
    }

    public DockSide DockSide
    {
        get => _settings.DockSide;
        set => RememberDock(value, _settings.DockOffset);
    }

    public CollapseStyle CollapseStyle
    {
        get => _settings.CollapseStyle;
        set => ChangeSettings(_settings with { CollapseStyle = value });
    }

    public bool IsOrbStyle => _settings.CollapseStyle == CollapseStyle.Orb;

    public ColorPalette Palette
    {
        get => _settings.Palette;
        set => ChangeSettings(_settings with { Palette = value });
    }

    public SurfaceMaterial Material
    {
        get => _settings.Material;
        // Each see-through material starts at its own recommended opacity; the slider then adjusts it.
        set => ChangeSettings(_settings with
        {
            Material = value,
            BackgroundOpacity = value == _settings.Material ? _settings.BackgroundOpacity : value switch
            {
                SurfaceMaterial.Glass => GlassDefaultOpacity,
                SurfaceMaterial.Clear => ClearDefaultOpacity,
                _ => _settings.BackgroundOpacity,
            },
        });
    }

    public const double GlassDefaultOpacity = 0.6;
    public const double ClearDefaultOpacity = 0.45;

    public bool IsTranslucent => _settings.Material != SurfaceMaterial.Solid;

    public double SurfaceOpacity => _settings.GetSurfaceOpacity();

    public void RememberDock(DockSide side, double offset)
        => ChangeSettings(_settings with { DockSide = side, DockOffset = offset });

    public async Task<bool> SelectListAsync(Guid listId)
    {
        if (listId == _listId) return true;
        if (!await FinishEditingAsync()) return false;
        // Commit the quick-add draft to its original list before changing selection.
        if (!string.IsNullOrWhiteSpace(NewTitle) && !await AddAsync()) return false;
        _listId = listId;
        ChangeSettings(_settings with { SelectedListId = listId });
        RefreshRows();
        return true;
    }

    public Task<bool> CreateListAsync(string name)
    {
        var requestId = Guid.NewGuid();
        return RunAsync(async () =>
        {
            var list = await _service.CreateListAsync(requestId, name);
            Store.ApplyList(list);
            _listId = list.Id;
            ChangeSettings(_settings with { SelectedListId = list.Id });
            RefreshRows();
        });
    }

    public Task<bool> RenameListAsync(string name)
    {
        var listId = _listId;
        return RunAsync(async () => Store.ApplyList(await _service.RenameListAsync(listId, name)));
    }

    public void ReloadSettings()
    {
        ChangeSettings(Store.Snapshot.Settings);
        _listId = Store.Snapshot.Lists.FirstOrDefault(list => list.Id == _settings.SelectedListId)?.Id
            ?? Store.Snapshot.Lists[0].Id;
        RefreshRows();
    }

    public Task<bool> AddAsync()
    {
        return RunAsync(async () =>
        {
            var title = NewTitle;
            var id = _createRequestId;
            Store.ApplyTask(await _service.CreateTaskAsync(id, _listId, title));
            if (NewTitle == title) NewTitle = "";
        });
    }

    public async Task<bool> BeginEditAsync(TaskRowViewModel row)
    {
        if (!CanInteract) return false;
        if (EditingTask == row) return true;
        if (!await CommitEditAsync()) return false;
        EditingTask = row;
        row.EditTitle = row.Title;
        row.IsEditing = true;
        return true;
    }

    public Task<bool> CommitEditAsync()
    {
        if (EditingTask is not { } row) return Task.FromResult(true);
        return RunAsync(async () =>
        {
            Store.ApplyTask(await _service.UpdateTaskTitleAsync(row.Id, row.EditTitle));
            row.IsEditing = false;
            EditingTask = null;
        });
    }

    public void CancelEdit()
    {
        if (IsSaving || EditingTask is not { } row) return;
        row.EditTitle = row.Title;
        row.IsEditing = false;
        EditingTask = null;
        _retry = null;
        ErrorMessage = null;
        RetryCommand.NotifyCanExecuteChanged();
    }

    public Task<bool> SetCompletedAsync(TaskRowViewModel row, bool completed)
        => RunAsync(async () => Store.ApplyTask(await _service.SetCompletedAsync(row.Id, completed)));

    public Task<bool> DeleteAsync(TaskRowViewModel row)
        => RunAsync(async () =>
        {
            Store.ApplyTask(await _service.SoftDeleteTaskAsync(row.Id));
            _undoId = row.Id;
            _undoDeadline = _clock.GetUtcNow().AddSeconds(8);
            RefreshUndo();
        });

    public Task<bool> UndoAsync()
    {
        RefreshUndo();
        if (!CanUndo) return Task.FromResult(false);
        var id = _undoId!.Value;
        return RunAsync(async () =>
        {
            Store.ApplyTask(await _service.RestoreTaskAsync(id));
            _undoId = null;
            RefreshUndo();
        });
    }

    public void RefreshUndo()
    {
        if (_clock.GetUtcNow() >= _undoDeadline) _undoId = null;
        OnPropertyChanged(nameof(CanUndo));
        UndoCommand.NotifyCanExecuteChanged();
    }

    public Task<bool> MoveAsync(Guid sourceId, Guid targetId, bool after)
    {
        var listId = _listId;
        var ids = ActiveTasks.Select(row => row.Id).ToList();
        if (sourceId == targetId || !ids.Contains(sourceId) || !ids.Contains(targetId))
            return Task.FromResult(false);
        ids.Remove(sourceId);
        ids.Insert(ids.IndexOf(targetId) + (after ? 1 : 0), sourceId);
        return RunAsync(async () =>
        {
            await _service.ReorderActiveTasksAsync(listId, ids);
            Store.ApplySnapshot(await _service.GetSnapshotAsync());
        });
    }

    public void RememberWindow(WindowPlacementSnapshot placement, WindowPresentationState state)
        => ChangeSettings(_settings with
        {
            WindowPlacement = placement,
            LastPresentationState = state is WindowPresentationState.Docked
                ? WindowPresentationState.Docked : WindowPresentationState.Expanded,
        });

    public Task<bool> SaveSettingsAsync()
    {
        if (_settings == Store.Snapshot.Settings) return Task.FromResult(true);
        return RunAsync(async () =>
        {
            var settings = _settings;
            await _service.SaveSettingsAsync(settings);
            Store.ApplySettings(settings);
        });
    }

    public Task<bool> RetryAsync()
        => _retry is { } operation ? RunAsync(operation) : Task.FromResult(true);

    // A transition can arrive while an editor is saving after losing focus.
    public async Task<bool> FinishEditingAsync()
    {
        if (IsSaving && !await _pendingOperation) return false;
        if (EditingTask is { } row && string.IsNullOrWhiteSpace(row.EditTitle))
        {
            CancelEdit();
            ErrorMessage = "空标题未保存，已保留原标题。";
            return true;
        }
        return await CommitEditAsync();
    }

    public async Task<bool> PrepareExitAsync()
    {
        IsClosing = true;
        var success = false;
        try
        {
            if (!await FinishEditingAsync()) return false;
            if (!string.IsNullOrWhiteSpace(NewTitle) && !await AddAsync()) return false;
            if (!await SaveSettingsAsync()) return false;
            success = await RunAsync(() => _service.FlushPendingWritesAsync());
            return success;
        }
        finally
        {
            if (!success) IsClosing = false;
        }
    }

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
        finally
        {
            IsSaving = false;
        }
    }

    private void ChangeSettings(SettingsSnapshot settings)
    {
        if (_settings == settings) return;
        _settings = settings;
        OnPropertyChanged(nameof(BackgroundOpacity));
        OnPropertyChanged(nameof(IsAlwaysOnTop));
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(DockSide));
        OnPropertyChanged(nameof(CollapseStyle));
        OnPropertyChanged(nameof(IsOrbStyle));
        OnPropertyChanged(nameof(Palette));
        OnPropertyChanged(nameof(Material));
        OnPropertyChanged(nameof(IsTranslucent));
        OnPropertyChanged(nameof(SurfaceOpacity));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(_renderedTasks, Store.Snapshot.Tasks) || !ReferenceEquals(_renderedLists, Store.Snapshot.Lists))
            RefreshRows();
    }

    private void DataReplaced(object? sender, EventArgs e)
    {
        EditingTask = null;
        _undoId = null;
        _retry = null;
        ErrorMessage = null;
        NewTitle = "";
        RefreshUndo();
        RetryCommand.NotifyCanExecuteChanged();
        ReloadSettings();
    }

    private void RefreshRows()
    {
        if (!Store.Snapshot.Lists.Any(list => list.Id == _listId))
            _listId = Store.Snapshot.Lists[0].Id;
        var existingLists = Lists.ToDictionary(list => list.Id);
        var lists = Store.Snapshot.Lists.Select(list =>
        {
            var item = existingLists.GetValueOrDefault(list.Id)
                ?? new TaskListViewModel(list, Store.Snapshot.GetIncompleteCount(list.Id));
            item.Apply(list, Store.Snapshot.GetIncompleteCount(list.Id));
            item.IsSelected = list.Id == _listId;
            return item;
        }).ToArray();
        Synchronize(Lists, lists);
        var existing = ActiveTasks.Concat(CompletedTasks).ToDictionary(row => row.Id);
        var rows = Store.Snapshot.Tasks.Where(task => task.ListId == _listId)
            .OrderBy(task => task.SortOrder).ThenBy(task => task.Id)
            .Select(task =>
            {
                var row = existing.GetValueOrDefault(task.Id) ?? new TaskRowViewModel(task);
                row.Apply(task);
                return row;
            }).ToArray();
        var active = rows.Where(row => !row.IsCompleted).ToArray();
        var completed = rows.Where(row => row.IsCompleted).ToArray();
        if (_renderedListId != _listId)
        {
            // Switching lists publishes one collection change rather than a change per task.
            ActiveTasks = new ObservableCollection<TaskRowViewModel>(active);
            CompletedTasks = new ObservableCollection<TaskRowViewModel>(completed);
            OnPropertyChanged(nameof(ActiveTasks));
            OnPropertyChanged(nameof(CompletedTasks));
        }
        else
        {
            Synchronize(ActiveTasks, active);
            Synchronize(CompletedTasks, completed);
        }
        _renderedListId = _listId;
        _renderedTasks = Store.Snapshot.Tasks;
        _renderedLists = Store.Snapshot.Lists;
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingSummary));
        OnPropertyChanged(nameof(CompletedSummary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CompletionRatio));
        OnPropertyChanged(nameof(ListName));
        OnPropertyChanged(nameof(SelectedListId));
    }

    private static void Synchronize<T>(ObservableCollection<T> collection, T[] rows)
    {
        foreach (var removed in collection.Except(rows).ToArray()) collection.Remove(removed);
        for (var index = 0; index < rows.Length; index++)
        {
            var oldIndex = collection.IndexOf(rows[index]);
            if (oldIndex < 0) collection.Insert(index, rows[index]);
            else if (oldIndex != index) collection.Move(oldIndex, index);
        }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(IsInputReadOnly));
        AddCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        Store.PropertyChanged -= StoreChanged;
        Store.DataReplaced -= DataReplaced;
    }
}
