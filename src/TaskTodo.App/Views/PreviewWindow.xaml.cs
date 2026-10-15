using System.Windows;
using System.Windows.Media.Animation;
using TaskTodo.App.Controls;
using TaskTodo.App.Desktop;
using TaskTodo.App.ViewModels;
using TaskTodo.Core.Interaction;

namespace TaskTodo.App.Views;

public partial class PreviewWindow : PassiveWindow
{
    public PreviewWindow(PreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => UpdateGlass();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PreviewViewModel.Material)) UpdateGlass();
        };
    }

    private void UpdateGlass()
    {
        if (PresentationSource.FromVisual(this) is null) return;
        GlassBackdrop.Apply(this, ((PreviewViewModel)DataContext).Material);
    }

    public event EventHandler? OpenRequested;

    public void Reveal()
    {
        var wasVisible = IsVisible;
        Show();
        if (!wasVisible && SystemParameters.ClientAreaAnimation)
            PreviewSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, EdgeInteractionTiming.Animation));
    }

    private void Open_OnClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private async void Completion_OnRequested(object? sender, CompletionRequestedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskRowViewModel row })
            await ((PreviewViewModel)DataContext).SetCompletedAsync(row, e.IsCompleted);
    }
}
