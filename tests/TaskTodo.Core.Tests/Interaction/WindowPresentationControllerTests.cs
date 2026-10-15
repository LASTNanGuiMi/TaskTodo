using TaskTodo.Core.Interaction;

namespace TaskTodo.Core.Tests.Interaction;

public sealed class WindowPresentationControllerTests
{
    [Fact]
    public void StartsExpanded_AndSupportsDockHideAndRecovery()
    {
        var controller = new WindowPresentationController();

        Assert.Equal(WindowPresentationState.Expanded, controller.State);
        Assert.Equal(WindowPresentationState.Docked, controller.Apply(WindowPresentationAction.Dock));
        Assert.Equal(WindowPresentationState.HiddenToTray, controller.Apply(WindowPresentationAction.HideToTray));
        Assert.Equal(WindowPresentationState.Expanded, controller.Apply(WindowPresentationAction.ShowOrRecover));
    }

    [Fact]
    public void PreviewCanOnlyOpenFromDocked_AndReturnsToDocked()
    {
        var controller = new WindowPresentationController();

        Assert.Equal(WindowPresentationState.Expanded, controller.Apply(WindowPresentationAction.BeginPreview));
        controller.Apply(WindowPresentationAction.Dock);
        Assert.Equal(WindowPresentationState.Previewing, controller.Apply(WindowPresentationAction.BeginPreview));
        Assert.Equal(WindowPresentationState.Docked, controller.Apply(WindowPresentationAction.EndPreview));
    }

    [Fact]
    public void RecoveringFromPreviewAlwaysShowsExpandedPanel()
    {
        var controller = new WindowPresentationController();
        controller.Apply(WindowPresentationAction.Dock);
        controller.Apply(WindowPresentationAction.BeginPreview);

        Assert.Equal(WindowPresentationState.Expanded, controller.Apply(WindowPresentationAction.ShowOrRecover));
    }
}
