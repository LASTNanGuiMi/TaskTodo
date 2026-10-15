namespace TaskTodo.App.Controls;

// Both mouse/keyboard clicks and accessibility Toggle requests use this path.
// The binding changes only when the service has committed the requested value.
public sealed class TaskCheckBox : System.Windows.Controls.CheckBox
{
    public event EventHandler<CompletionRequestedEventArgs>? CompletionRequested;

    protected override void OnToggle()
        => CompletionRequested?.Invoke(this, new CompletionRequestedEventArgs(IsChecked != true));
}

public sealed class CompletionRequestedEventArgs(bool isCompleted) : EventArgs
{
    public bool IsCompleted { get; } = isCompleted;
}
