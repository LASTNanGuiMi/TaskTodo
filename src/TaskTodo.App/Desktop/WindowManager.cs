using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskTodo.App.ViewModels;
using TaskTodo.App.Views;
using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;

namespace TaskTodo.App.Desktop;

internal sealed class WindowManager : IDisposable
{
    private readonly TaskTodoService _service;
    private readonly MainWindowViewModel _viewModel;
    private readonly PreviewViewModel _previewViewModel;
    private readonly WindowPresentationController _presentation = new();
    private readonly EdgeHoverController _hover = new();
    private readonly MonitorService _monitors = new();
    private readonly MainWindow _mainWindow;
    private readonly DockRailWindow _dockWindow;
    private readonly PreviewWindow _previewWindow;
    private readonly TrayService _tray;
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly WinEventCallback _foregroundChanged;
    private readonly IntPtr _foregroundHook;
    private WindowPresentationState _lastVisibleState;
    private (ThemePreference Theme, ColorPalette Palette, SurfaceMaterial Material) _appliedTheme;
    private CollapseStyle _appliedCollapseStyle;
    private bool _initialized;
    private bool _settingsDirty;
    private bool _transitioning;
    private Guid? _queuedOpenListId;
    private bool _layoutQueued;
    private bool _disposed;

    public WindowManager(TaskTodoService service, MainWindowViewModel viewModel)
    {
        _service = service;
        _viewModel = viewModel;
        _lastVisibleState = viewModel.Settings.LastPresentationState;
        _appliedTheme = (viewModel.Theme, viewModel.Palette, viewModel.Material);
        _appliedCollapseStyle = viewModel.CollapseStyle;
        ThemeService.Apply(_appliedTheme.Theme, _appliedTheme.Palette, _appliedTheme.Material);
        _mainWindow = new MainWindow(viewModel);
        _dockWindow = new DockRailWindow { DataContext = viewModel };
        _previewViewModel = new PreviewViewModel(service, viewModel.Store);
        _previewWindow = new PreviewWindow(_previewViewModel);
        System.Windows.Application.Current.MainWindow = _mainWindow;
        _mainWindow.DockRequested += (_, _) => ChangePresentation(WindowPresentationAction.Dock);
        _mainWindow.BoundsChanged += (_, _) => ScheduleSettings();
        _mainWindow.DataToolsRequested += (_, _) => DataToolsRequested?.Invoke(_mainWindow, EventArgs.Empty);
        _dockWindow.OpenRequested += async (_, e) => await OpenListAsync(e.ListId);
        _dockWindow.HoverRequested += (_, e) => _hover.EnterCapsule(e.ListId, DateTimeOffset.UtcNow);
        _dockWindow.HideToTrayRequested += (_, _) => ChangePresentation(WindowPresentationAction.HideToTray);
        _dockWindow.DragStarted += (_, _) => { HidePreview(); _hover.Start(); };
        _dockWindow.DragDelta += (_, e) => _monitors.MoveDock(_dockWindow, e.HorizontalChange, e.VerticalChange);
        _dockWindow.DragCompleted += (_, _) =>
        {
            var placement = _monitors.SnapDock(_dockWindow, _mainWindow);
            _viewModel.RememberDock(placement.Side, placement.Offset);
            PlaceDock();
            ScheduleSettings();
        };
        _dockWindow.StyleRequested += (_, e) => _viewModel.CollapseStyle = e.Style;
        _dockWindow.SideRequested += (_, e) =>
        {
            _viewModel.RememberDock(e.Side, _viewModel.Settings.DockOffset);
            PlaceDock();
            HidePreview();
            _hover.Start();
        };
        _previewWindow.OpenRequested += async (_, _) => await OpenListAsync(_previewViewModel.ListId);
        _viewModel.SettingsChanged += SettingsChanged;
        _viewModel.PropertyChanged += ViewModelChanged;
        _viewModel.Lists.CollectionChanged += ListsChanged;
        _previewViewModel.PropertyChanged += PreviewChanged;
        _settingsTimer.Tick += SaveSettings;
        _undoTimer.Tick += (_, _) => _viewModel.RefreshUndo();
        _hoverTimer.Tick += HoverTick;
        // A full-screen app that is itself topmost covers the rail once it is activated, and a window that never
        // activates does not come back on its own: re-assert its place on each foreground change and every 2 s.
        _topmostTimer.Tick += (_, _) => KeepRailOnTop();
        _foregroundChanged = (_, _, _, _, _, _, _) => KeepRailOnTop();
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _foregroundChanged,
            0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        _monitors.EnvironmentChanged += (_, _) => QueueDisplayRecovery();
        _monitors.BoundsSettled += (_, _) => SaveSettings(this, EventArgs.Empty);
        _monitors.Observe(_mainWindow);
        _monitors.Observe(_dockWindow);
        _monitors.Observe(_previewWindow);
        _tray = new TrayService(
            ShowOrRecover,
            () => ChangePresentation(WindowPresentationAction.Dock),
            () => ChangePresentation(WindowPresentationAction.HideToTray),
            ExitApplication);
    }

    public event EventHandler? DataToolsRequested;

    public void Initialize()
    {
        if (_viewModel.Settings.WindowPlacement is { } placement)
            _monitors.Restore(_mainWindow, placement);
        if (_lastVisibleState == WindowPresentationState.Docked) DockCore();
        else ShowCore();
        _initialized = true;
    }

    public async void ShowOrRecover()
    {
        if (_transitioning || ActivateOwnedDialog()) return;
        if (_previewViewModel.IsSaving) await _previewViewModel.PendingOperation;
        if (_previewViewModel.HasError) { _previewWindow.Reveal(); return; }
        ShowCore();
        ScheduleSettings();
    }

    private async Task OpenListAsync(Guid id)
    {
        if (_transitioning)
        {
            // The capsule is already visible while the dock transition is still saving settings; a click landing
            // in that window must not be lost. Keep the latest request and replay it when the transition ends.
            _queuedOpenListId = id;
            return;
        }
        _transitioning = true;
        try
        {
            if (_previewViewModel.IsSaving) await _previewViewModel.PendingOperation;
            if (_previewViewModel.HasError) return;
            if (await _viewModel.SelectListAsync(id))
            {
                _viewModel.IsSettingsOpen = false;
                ShowCore();
                ScheduleSettings();
            }
        }
        finally { EndTransition(); }
    }

    private void EndTransition()
    {
        _transitioning = false;
        if (_queuedOpenListId is not { } id) return;
        _queuedOpenListId = null;
        // Replay only while the capsule or preview that raised the click is still on screen.
        if (_disposed || !(_dockWindow.IsVisible || _previewWindow.IsVisible)) return;
        _mainWindow.Dispatcher.InvokeAsync(() => ReplayQueuedOpen(id));
    }

    private async void ReplayQueuedOpen(Guid id) => await OpenListAsync(id);

    private void ShowCore()
    {
        StopHover();
        _presentation.Apply(WindowPresentationAction.ShowOrRecover);
        _lastVisibleState = WindowPresentationState.Expanded;
        _dockWindow.Hide();
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _monitors.ConstrainToWorkArea(_mainWindow);
        _mainWindow.Activate();
    }

    private async void ChangePresentation(WindowPresentationAction action)
    {
        if (_transitioning || _mainWindow.HasActiveComposition || ActivateOwnedDialog()) return;
        _transitioning = true;
        try
        {
            if (_previewViewModel.IsSaving) await _previewViewModel.PendingOperation;
            if (_previewViewModel.HasError) return;
            if (!await _viewModel.FinishEditingAsync())
            {
                ShowCore();
                return;
            }
            if (action == WindowPresentationAction.Dock) DockCore();
            else
            {
                StopHover();
                _presentation.Apply(action);
                _dockWindow.Hide();
                _mainWindow.Hide();
            }
            RememberWindow();
            await _viewModel.SaveSettingsAsync();
            if (_viewModel.HasError) ShowCore();
        }
        finally { EndTransition(); }
    }

    private void DockCore()
    {
        HidePreview();
        _presentation.Apply(WindowPresentationAction.Dock);
        _lastVisibleState = WindowPresentationState.Docked;
        _monitors.ConstrainToWorkArea(_mainWindow);
        PlaceDock();
        _dockWindow.Show();
        _mainWindow.Hide();
        _hover.Start();
        _hoverTimer.Start();
        _topmostTimer.Start();
        KeepRailOnTop();
    }

    private void PlaceDock()
        => _monitors.PlaceDock(_dockWindow, _mainWindow, _viewModel.Settings.DockSide,
            _viewModel.Settings.DockOffset, _viewModel.Lists.Count, _viewModel.CollapseStyle);

    private void PlacePreview()
    {
        var height = Math.Clamp(90 + _previewViewModel.Rows.Sum(row => row.Title.Length > 30 ? 62 : 36)
            + (_previewViewModel.HasError ? 90 : 0), 140, 420);
        _monitors.PlacePreview(_previewWindow, _dockWindow, _viewModel.Settings.DockSide,
            height, _dockWindow.HoverAnchorTop);
    }

    private void HoverTick(object? sender, EventArgs e)
    {
        var inside = _monitors.CursorInHoverRegion(_dockWindow, _previewWindow.IsVisible ? _previewWindow : null);
        var busy = _transitioning || _dockWindow.IsDragging || _dockWindow.IsMenuOpen || Mouse.Captured is not null
            || _previewViewModel.IsSaving || _previewViewModel.HasError || _viewModel.IsSaving
            || (_mainWindow.IsVisible && (_viewModel.IsSettingsOpen || _viewModel.EditingTask is not null));
        var previous = _hover.VisibleListId;
        var now = DateTimeOffset.UtcNow;
        _hover.ObserveRegion(inside, busy, now);
        _hover.Tick(now);
        if (_hover.VisibleListId == previous) return;
        if (_hover.VisibleListId is { } id)
        {
            _previewViewModel.SelectList(id);
            PlacePreview();
            _presentation.Apply(WindowPresentationAction.BeginPreview);
            _previewWindow.Reveal();
        }
        else HidePreview();
    }

    private void HidePreview()
    {
        _previewWindow.Hide();
        _presentation.Apply(WindowPresentationAction.EndPreview);
    }

    private void StopHover()
    {
        _hoverTimer.Stop();
        _topmostTimer.Stop();
        _hover.Reset();
        HidePreview();
    }

    private void SettingsChanged(object? sender, EventArgs e)
    {
        var theme = (_viewModel.Theme, _viewModel.Palette, _viewModel.Material);
        if (theme != _appliedTheme)
        {
            _appliedTheme = theme;
            ThemeService.Apply(theme.Theme, theme.Palette, theme.Material);
        }
        if (_viewModel.CollapseStyle != _appliedCollapseStyle)
        {
            _appliedCollapseStyle = _viewModel.CollapseStyle;
            if (_dockWindow.IsVisible && !_dockWindow.IsDragging)
            {
                HidePreview();
                PlaceDock();
                _hover.Start();
            }
        }
        ScheduleSettings();
    }

    private void ListsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_dockWindow.IsVisible && !_dockWindow.IsDragging) PlaceDock();
    }

    private void PreviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_previewWindow.IsVisible && e.PropertyName is nameof(PreviewViewModel.PendingSummary) or nameof(PreviewViewModel.HasError))
            PlacePreview();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CanUndo))
        {
            if (_viewModel.CanUndo) _undoTimer.Start();
            else _undoTimer.Stop();
        }
        if (e.PropertyName == nameof(MainWindowViewModel.HasError) && !_viewModel.HasError && _settingsDirty)
            _settingsTimer.Start();
    }

    private void QueueDisplayRecovery()
    {
        if (!_initialized || _layoutQueued || _disposed) return;
        _layoutQueued = true;
        _mainWindow.Dispatcher.BeginInvoke(() =>
        {
            _layoutQueued = false;
            if (_disposed) return;
            if (_viewModel.Settings.WindowPlacement is { } placement && !_monitors.HasMonitor(placement.MonitorId))
                _monitors.Restore(_mainWindow, placement);
            else _monitors.ConstrainToWorkArea(_mainWindow);
            if (_dockWindow.IsVisible && !_dockWindow.IsDragging) PlaceDock();
            if (_previewWindow.IsVisible) PlacePreview();
            ScheduleSettings();
        }, DispatcherPriority.Loaded);
    }

    private void ScheduleSettings()
    {
        if (!_initialized || _disposed) return;
        _settingsDirty = true;
        _settingsTimer.Stop();
        _settingsTimer.Start();
    }

    private async void SaveSettings(object? sender, EventArgs e)
    {
        _settingsTimer.Stop();
        if (_transitioning || _viewModel.IsSaving || _previewViewModel.IsSaving || _viewModel.EditingTask is not null
            || _mainWindow.OwnedWindows.Cast<Window>().Any(window => window.IsVisible))
        {
            _settingsTimer.Start();
            return;
        }
        if (_viewModel.HasError) return;
        _monitors.ConstrainToWorkArea(_mainWindow);
        RememberWindow();
        _settingsDirty = false;
        await _viewModel.SaveSettingsAsync();
    }

    private void RememberWindow()
        => _viewModel.RememberWindow(_monitors.Capture(_mainWindow), _lastVisibleState);

    public async Task<bool> PrepareShutdownAsync()
    {
        if (_disposed) return true;
        if (_transitioning || _mainWindow.HasActiveComposition || ActivateOwnedDialog()) return false;
        _transitioning = true;
        _settingsTimer.Stop();
        if (_previewViewModel.IsSaving) await _previewViewModel.PendingOperation;
        if (_previewViewModel.HasError)
        {
            EndTransition();
            return false;
        }
        RememberWindow();
        if (!await _viewModel.PrepareExitAsync())
        {
            EndTransition();
            ShowCore();
            return false;
        }
        await _service.StopAsync();
        Dispose();
        return true;
    }

    private async void ExitApplication()
    {
        if (await PrepareShutdownAsync()) System.Windows.Application.Current.Shutdown();
    }

    private bool ActivateOwnedDialog()
    {
        var dialog = _mainWindow.OwnedWindows.Cast<Window>().FirstOrDefault(window => window.IsVisible);
        if (dialog is null) return false;
        dialog.Activate();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settingsTimer.Stop();
        _undoTimer.Stop();
        _hoverTimer.Stop();
        _topmostTimer.Stop();
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        _monitors.Dispose();
        _viewModel.SettingsChanged -= SettingsChanged;
        _viewModel.PropertyChanged -= ViewModelChanged;
        _viewModel.Lists.CollectionChanged -= ListsChanged;
        _previewViewModel.PropertyChanged -= PreviewChanged;
        _previewViewModel.Dispose();
        _viewModel.Dispose();
        _tray.Dispose();
        _previewWindow.Close();
        _dockWindow.AllowApplicationExit();
        _dockWindow.Close();
        _mainWindow.AllowApplicationExit();
        _mainWindow.Close();
    }

    private void KeepRailOnTop()
    {
        if (_disposed || !_viewModel.IsAlwaysOnTop || !_dockWindow.IsVisible || _dockWindow.IsDragging) return;
        foreach (var window in new Window[] { _dockWindow, _previewWindow })
        {
            if (!window.IsVisible) continue;
            SetWindowPos(new WindowInteropHelper(window).Handle, TopmostInsertAfter, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
        }
    }

    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly IntPtr TopmostInsertAfter = new(-1);

    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId,
        uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventCallback callback,
        uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
