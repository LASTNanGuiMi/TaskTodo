using TaskTodo.Core.Interaction;
using TaskTodo.Core.Models;

namespace TaskTodo.Core.Tests.Interaction;

public sealed class EdgeHoverTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T00:00:00Z");

    [Fact]
    public void Hover_waits_and_switching_replaces_the_previous_deadline()
    {
        var hover = new EdgeHoverController();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        hover.Start();
        hover.EnterCapsule(first, Now);
        hover.Tick(Now.AddMilliseconds(149));
        Assert.Null(hover.VisibleListId);
        hover.EnterCapsule(second, Now.AddMilliseconds(100));
        hover.Tick(Now.AddMilliseconds(150));
        Assert.Null(hover.VisibleListId);
        hover.Tick(Now.AddMilliseconds(250));
        Assert.Equal(second, hover.VisibleListId);
    }

    [Fact]
    public void Reentry_and_busy_interactions_cancel_leave_and_require_a_fresh_delay()
    {
        var hover = new EdgeHoverController();
        var id = Guid.NewGuid();
        hover.Start();
        hover.EnterCapsule(id, Now);
        hover.Tick(Now.AddMilliseconds(150));
        hover.ObserveRegion(false, false, Now.AddMilliseconds(200));
        hover.ObserveRegion(true, false, Now.AddMilliseconds(400));
        hover.Tick(Now.AddMilliseconds(600));
        Assert.Equal(id, hover.VisibleListId);
        hover.ObserveRegion(false, true, Now.AddMilliseconds(700));
        hover.Tick(Now.AddMilliseconds(1500));
        Assert.Equal(id, hover.VisibleListId);
        hover.ObserveRegion(false, false, Now.AddMilliseconds(1500));
        hover.Tick(Now.AddMilliseconds(1849));
        Assert.Equal(id, hover.VisibleListId);
        hover.Tick(Now.AddMilliseconds(1850));
        Assert.Null(hover.VisibleListId);
    }

    [Fact]
    public void Leaving_before_open_and_reset_prevent_obsolete_preview()
    {
        var hover = new EdgeHoverController();
        hover.Start();
        hover.EnterCapsule(Guid.NewGuid(), Now);
        hover.ObserveRegion(false, false, Now.AddMilliseconds(50));
        hover.Tick(Now.AddSeconds(1));
        Assert.Null(hover.VisibleListId);
        hover.EnterCapsule(Guid.NewGuid(), Now.AddSeconds(2));
        hover.Reset();
        hover.Tick(Now.AddSeconds(3));
        hover.EnterCapsule(Guid.NewGuid(), Now.AddSeconds(4));
        hover.Tick(Now.AddSeconds(5));
        Assert.Null(hover.VisibleListId);
    }

    [Theory]
    [InlineData(DockSide.Left)]
    [InlineData(DockSide.Right)]
    public void Dock_and_preview_fit_negative_workarea_and_bridge_the_gap(DockSide side)
    {
        var workArea = new WindowBounds(-1920, -120, 1920, 1040);
        var dock = EdgePlacement.Dock(workArea, 128, 80, side, 0.9);
        var preview = EdgePlacement.Preview(dock, workArea, side, 420);
        Assert.True(preview.Left >= workArea.Left && preview.Right <= workArea.Right);
        Assert.True(preview.Top >= workArea.Top && preview.Bottom <= workArea.Bottom);
        var x = side == DockSide.Left ? dock.Right + 4 : dock.Left - 4;
        Assert.True(EdgePlacement.InHoverRegion(x, dock.Top + 10, dock, preview));
        Assert.False(EdgePlacement.InHoverRegion(x, workArea.Top, dock, preview));
        Assert.Equal(0.9, EdgePlacement.Offset(dock, workArea), 5);
    }

    [Fact]
    public void Many_capsules_are_constrained_to_workarea_and_tiny_preview_still_fits()
    {
        var workArea = new WindowBounds(100, 20, 240, 180);
        var dock = EdgePlacement.Dock(workArea, 128, 2000, DockSide.Right, 0.5);
        Assert.Equal(workArea.Height, dock.Height);
        Assert.Equal(0, EdgePlacement.Offset(dock, workArea));
        var preview = EdgePlacement.Preview(dock, workArea, DockSide.Right, 420);
        Assert.Equal(workArea, preview);
    }
}
