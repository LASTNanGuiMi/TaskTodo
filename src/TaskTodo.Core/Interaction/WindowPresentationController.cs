namespace TaskTodo.Core.Interaction;

/// <summary>
/// Keeps presentation state independent from WPF window lifetimes.
/// M1 uses Expanded, Docked and HiddenToTray; Previewing is reserved for M4.
/// </summary>
public sealed class WindowPresentationController
{
    public WindowPresentationState State { get; private set; } = WindowPresentationState.Expanded;

    public WindowPresentationState Apply(WindowPresentationAction action)
    {
        State = action switch
        {
            WindowPresentationAction.ShowOrRecover => WindowPresentationState.Expanded,
            WindowPresentationAction.Dock => WindowPresentationState.Docked,
            WindowPresentationAction.HideToTray => WindowPresentationState.HiddenToTray,
            WindowPresentationAction.BeginPreview when State == WindowPresentationState.Docked
                => WindowPresentationState.Previewing,
            WindowPresentationAction.EndPreview when State == WindowPresentationState.Previewing
                => WindowPresentationState.Docked,
            _ => State,
        };

        return State;
    }
}
