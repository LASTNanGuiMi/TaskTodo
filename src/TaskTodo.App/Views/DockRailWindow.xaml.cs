using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TaskTodo.App.Desktop;
using TaskTodo.App.ViewModels;
using TaskTodo.Core.Models;

namespace TaskTodo.App.Views;

public partial class DockRailWindow : PassiveWindow
{
    private readonly PointerDrag _drag = new();
    private bool _allowApplicationExit;

    public DockRailWindow()
    {
        InitializeComponent();
        _drag.Started += (_, _) =>
        {
            // A drag must not also count as a click on the orb.
            OrbButton.ReleaseMouseCapture();
            IsDragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
        };
        _drag.Moved += (_, e) => DragDelta?.Invoke(this, e);
        _drag.Finished += (_, dragged) =>
        {
            if (!dragged) return;
            IsDragging = false;
            DragCompleted?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler<ListRequestedEventArgs>? OpenRequested;
    public event EventHandler<ListRequestedEventArgs>? HoverRequested;
    public event EventHandler? HideToTrayRequested;
    public event EventHandler? DragStarted;
    public event EventHandler<DragDeltaEventArgs>? DragDelta;
    public event EventHandler? DragCompleted;
    public event EventHandler<DockSideRequestedEventArgs>? SideRequested;
    public event EventHandler<CollapseStyleRequestedEventArgs>? StyleRequested;
    public bool IsDragging { get; private set; }
    public bool IsMenuOpen { get; private set; }
    public double HoverAnchorTop { get; private set; }

    public void AllowApplicationExit() => _allowApplicationExit = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowApplicationExit)
        {
            e.Cancel = true;
            HideToTrayRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnClosing(e);
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
        => OpenRequested?.Invoke(this, new ListRequestedEventArgs(((TaskListViewModel)((FrameworkElement)sender).DataContext).Id));

    private void Capsule_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => Hover(sender);
    private void Capsule_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) => Hover(sender);

    private void Hover(object sender)
    {
        var element = (FrameworkElement)sender;
        HoverAnchorTop = element.PointToScreen(new System.Windows.Point()).Y;
        HoverRequested?.Invoke(this, new ListRequestedEventArgs(((TaskListViewModel)element.DataContext).Id));
    }

    // The grip only moves the rail, so the Thumb's own capture-based drag is replaced by the polled one.
    private void Grip_OnPreviewDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        _drag.Begin(this);
    }

    private void ContextMenu_OnOpening(object sender, ContextMenuEventArgs e)
    {
        IsMenuOpen = true;
        var orb = (DataContext as MainWindowViewModel)?.IsOrbStyle == true;
        OrbStyleItem.Visibility = orb ? Visibility.Collapsed : Visibility.Visible;
        CapsuleStyleItem.Visibility = orb ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Orb_OnStyleClick(object sender, RoutedEventArgs e) => StyleRequested?.Invoke(this, new CollapseStyleRequestedEventArgs(CollapseStyle.Orb));
    private void Capsule_OnStyleClick(object sender, RoutedEventArgs e) => StyleRequested?.Invoke(this, new CollapseStyleRequestedEventArgs(CollapseStyle.Capsule));

    // The orb is a single button: a press that travels past the system drag threshold moves the window along the edge
    // (same events the capsule grip raises), a press without travel is an ordinary click that opens the selected list.

    private Guid SelectedListId => ((MainWindowViewModel)DataContext).SelectedListId;

    private void Orb_OnClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, new ListRequestedEventArgs(SelectedListId));
    private void Orb_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => HoverOrb();
    private void Orb_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) => HoverOrb();

    private void HoverOrb()
    {
        if (_drag.IsActive) return;
        HoverAnchorTop = OrbButton.PointToScreen(new System.Windows.Point()).Y;
        HoverRequested?.Invoke(this, new ListRequestedEventArgs(SelectedListId));
    }

    private void Orb_OnPreviewDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _drag.Begin(this);
    private void ContextMenu_OnClosing(object sender, ContextMenuEventArgs e) => IsMenuOpen = false;
    private void Left_OnClick(object sender, RoutedEventArgs e) => SideRequested?.Invoke(this, new DockSideRequestedEventArgs(DockSide.Left));
    private void Right_OnClick(object sender, RoutedEventArgs e) => SideRequested?.Invoke(this, new DockSideRequestedEventArgs(DockSide.Right));
    private void Hide_OnClick(object sender, RoutedEventArgs e) => HideToTrayRequested?.Invoke(this, EventArgs.Empty);
}

public sealed class ListRequestedEventArgs(Guid listId) : EventArgs
{
    public Guid ListId { get; } = listId;
}

public sealed class CollapseStyleRequestedEventArgs(CollapseStyle style) : EventArgs
{
    public CollapseStyle Style { get; } = style;
}

public sealed class DockSideRequestedEventArgs(DockSide side) : EventArgs
{
    public DockSide Side { get; } = side;
}
