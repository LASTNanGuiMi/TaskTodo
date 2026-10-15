using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TaskTodo.App.Controls;
using TaskTodo.App.ViewModels;

namespace TaskTodo.App.Views;

public partial class MainWindow : Window
{
    private bool _allowApplicationExit;
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => UpdateGlass();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.Material)) UpdateGlass();
        };
        SizeChanged += (_, _) =>
        {
            UpdateLayoutDensity();
            BoundsChanged?.Invoke(this, EventArgs.Empty);
        };
        LocationChanged += (_, _) => BoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? DockRequested;
    public event EventHandler? BoundsChanged;
    public event EventHandler? DataToolsRequested;
    public bool HasActiveComposition => HasComposition(this);

    public void AllowApplicationExit() => _allowApplicationExit = true;

    private void UpdateLayoutDensity()
    {
        var compact = ActualHeight < 340;
        Resources["TaskRowPadding"] = new Thickness(8, compact ? 5 : 8, 4, compact ? 5 : 8);
        Resources["CompletedSectionWeight"] = new GridLength(compact ? 2 : 1, GridUnitType.Star);
        Header.Margin = compact ? new Thickness(10, 6, 8, 0) : new Thickness(10, 9, 8, 0);
        ContentArea.Margin = compact ? new Thickness(8, 4, 8, 0) : new Thickness(8, 10, 8, 0);
        PendingSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        EmptyIllustration.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        AddArea.Margin = compact ? new Thickness(10, 6, 10, 8) : new Thickness(10, 8, 10, 10);
    }

    private void UpdateGlass()
    {
        if (PresentationSource.FromVisual(this) is null) return;
        TaskTodo.App.Desktop.GlassBackdrop.Apply(this, _viewModel.Material);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the panel (the X button, Alt+F4) collapses it to the edge; only the tray menu hides everything.
        if (!_allowApplicationExit)
        {
            e.Cancel = true;
            DockRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control && !HasActiveComposition)
        {
            e.Handled = true;
            DockRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnPreviewKeyDown(e);
    }

    private void TitleDragRegion_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        DragMove();
        BoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DockButton_OnClick(object sender, RoutedEventArgs e)
        => DockRequested?.Invoke(this, EventArgs.Empty);


    private async void QuickAdd_OnSubmit(object? sender, EventArgs e) => await AddTaskAsync();
    private async void AddButton_OnClick(object sender, RoutedEventArgs e) => await AddTaskAsync();

    private async void ListPicker_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ListPicker.SelectedValue is not Guid id || id == _viewModel.SelectedListId) return;
        if (!await _viewModel.SelectListAsync(id))
            ListPicker.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, _viewModel.SelectedListId);
    }

    private async Task<bool> FinishDraftsAsync()
        => !HasActiveComposition && await _viewModel.FinishEditingAsync()
            && (string.IsNullOrWhiteSpace(_viewModel.NewTitle) || await _viewModel.AddAsync());

    private async void CreateList_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await FinishDraftsAsync()) return;
        new ListEditorWindow("新建清单", "", _viewModel.CreateListAsync, _viewModel) { Owner = this }.ShowDialog();
    }

    private async void RenameList_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await FinishDraftsAsync()) return;
        new ListEditorWindow("重命名清单", _viewModel.ListName, _viewModel.RenameListAsync, _viewModel) { Owner = this }.ShowDialog();
    }

    private async void DataTools_OnClick(object sender, RoutedEventArgs e)
    {
        if (await FinishDraftsAsync() && await _viewModel.SaveSettingsAsync())
            DataToolsRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task AddTaskAsync()
    {
        if (HasActiveComposition || !_viewModel.CanInteract) return;
        if (await _viewModel.FinishEditingAsync() && await _viewModel.AddAsync())
            QuickAdd.Focus();
    }

    private static bool HasComposition(DependencyObject element)
    {
        if (element is TaskTextBox { IsComposing: true }) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (HasComposition(VisualTreeHelper.GetChild(element, index))) return true;
        return false;
    }
}
