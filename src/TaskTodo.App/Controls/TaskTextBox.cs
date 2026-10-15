using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TaskTodo.App.Controls;

public class TaskTextBox : System.Windows.Controls.TextBox
{
    public TaskTextBox()
    {
        SetResourceReference(StyleProperty, typeof(System.Windows.Controls.TextBox));
        TextCompositionManager.AddPreviewTextInputStartHandler(this, (_, _) => IsComposing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(this, (_, _) => IsComposing = true);
        TextCompositionManager.AddPreviewTextInputHandler(this, (_, _) =>
        {
            // Candidate confirmation belongs to the IME, including its Enter/Esc key.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                IsComposing = false;
                if (IsLoaded) CompositionFinished?.Invoke(this, EventArgs.Empty);
            });
        });
        Unloaded += (_, _) => IsComposing = false;
    }

    private static readonly DependencyPropertyKey IsComposingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsComposing), typeof(bool), typeof(TaskTextBox), new PropertyMetadata(false));

    public static readonly DependencyProperty IsComposingProperty = IsComposingPropertyKey.DependencyProperty;

    // A dependency property so the quick-add hint can hide while the IME holds uncommitted text.
    public bool IsComposing
    {
        get => (bool)GetValue(IsComposingProperty);
        private set => SetValue(IsComposingPropertyKey, value);
    }
    public event EventHandler? SubmitRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? CompositionFinished;

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (!IsComposing && e.Key != Key.ImeProcessed && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SubmitRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        base.OnPreviewKeyDown(e);
    }
}
