using TaskTodo.Core.Interaction;

namespace TaskTodo.Core.Tests.Interaction;

public sealed class WorkAreaPlacementTests
{
    [Fact]
    public void Constrain_ClampsWindowIntoNegativeCoordinateWorkArea()
    {
        var workArea = new WindowBounds(-1920, 0, 1920, 1040);
        var desired = new WindowBounds(-2200, 900, 340, 440);

        var actual = WorkAreaPlacement.Constrain(desired, workArea, 280, 260);

        Assert.Equal(new WindowBounds(-1920, 600, 340, 440), actual);
    }

    [Fact]
    public void Constrain_ShrinksOversizedWindowToAvailableWorkArea()
    {
        var workArea = new WindowBounds(0, 0, 800, 560);
        var desired = new WindowBounds(50, 50, 1200, 900);

        var actual = WorkAreaPlacement.Constrain(desired, workArea, 280, 260);

        Assert.Equal(workArea, actual);
    }

    [Fact]
    public void Constrain_RejectsNonFiniteBounds()
    {
        var desired = new WindowBounds(double.NaN, 0, 340, 440);
        var workArea = new WindowBounds(0, 0, 1920, 1040);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => WorkAreaPlacement.Constrain(desired, workArea, 280, 260));
    }
}
