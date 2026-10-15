using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TaskTodo.App.ViewModels;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace TaskTodo.App.Controls;

public partial class TaskRow : System.Windows.Controls.UserControl
{
    private const string DragFormat = "TaskTodo.ActiveTask";
    private Point _dragStart;
    private TaskRowViewModel Row => (TaskRowViewModel)DataContext;
    private MainWindowViewModel ViewModel => (MainWindowViewModel)Window.GetWindow(this).DataContext;

    public TaskRow() => InitializeComponent();

    private async void Completion_OnRequested(object? sender, CompletionRequestedEventArgs e)
    {
        var row = Row;
        var viewModel = ViewModel;
        if (await viewModel.FinishEditingAsync())
            await viewModel.SetCompletedAsync(row, e.IsCompleted);
    }

    private async void Title_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        e.Handled = true;
        await ViewModel.BeginEditAsync(Row);
    }

    private void Menu_OnClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private async void Edit_OnClick(object sender, RoutedEventArgs e)
        => await ViewModel.BeginEditAsync(Row);

    private async void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        var row = Row;
        var viewModel = ViewModel;
        if (await viewModel.FinishEditingAsync())
            await viewModel.DeleteAsync(row);
    }

    private async void Editor_OnSubmit(object? sender, EventArgs e)
        => await ViewModel.CommitEditAsync();

    private void Editor_OnCancel(object? sender, EventArgs e) => ViewModel.CancelEdit();

    private async void Editor_OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Row.IsEditing && !Editor.IsComposing && !ViewModel.IsSaving)
            await ViewModel.CommitEditAsync();
    }

    private async void Editor_OnCompositionFinished(object? sender, EventArgs e)
    {
        if (Row.IsEditing && !Editor.IsKeyboardFocusWithin && !ViewModel.IsSaving)
            await ViewModel.CommitEditAsync();
    }

    private void Editor_OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Editor.IsVisible)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (!Editor.IsVisible) return;
                Editor.Focus();
                Editor.SelectAll();
            });
    }

    private void Handle_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        DragHandle.CaptureMouse();
        e.Handled = true;
    }

    private void Handle_OnMouseUp(object sender, MouseButtonEventArgs e)
        => DragHandle.ReleaseMouseCapture();

    private void Handle_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!DragHandle.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || !ViewModel.CanInteract
            || Row.IsCompleted || ViewModel.EditingTask is not null) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragHandle.ReleaseMouseCapture();
        DragDrop.DoDragDrop(DragHandle, new System.Windows.DataObject(DragFormat, Row.Id), System.Windows.DragDropEffects.Move);
    }

    private void Row_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = !Row.IsCompleted && ViewModel.CanInteract && e.Data.GetDataPresent(DragFormat)
            ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void Row_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (Row.IsCompleted || !ViewModel.CanInteract || !e.Data.GetDataPresent(DragFormat)) return;
        await ViewModel.MoveAsync((Guid)e.Data.GetData(DragFormat), Row.Id, e.GetPosition(this).Y > ActualHeight / 2);
    }
}
