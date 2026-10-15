using TaskTodo.Core.Models;
using TaskTodo.Core.Services;

namespace TaskTodo.Core.Tests;

public sealed class SettingsOpacityRuleTests
{
    [Theory]
    [InlineData(0.15)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Accepts_background_opacity_within_the_glass_range(double opacity)
        => SettingsRules.Validate(SettingsSnapshot.Default with { BackgroundOpacity = opacity });

    [Theory]
    [InlineData(0.14)]
    [InlineData(0.0)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Rejects_background_opacity_outside_the_range(double opacity)
    {
        var error = Assert.Throws<TaskTodoException>(
            () => SettingsRules.Validate(SettingsSnapshot.Default with { BackgroundOpacity = opacity }));
        Assert.Equal(TaskTodoErrorCode.Validation, error.Code);
    }

    [Fact]
    public void Rejects_an_undefined_collapse_style()
    {
        var error = Assert.Throws<TaskTodoException>(
            () => SettingsRules.Validate(SettingsSnapshot.Default with { CollapseStyle = (CollapseStyle)99 }));
        Assert.Equal(TaskTodoErrorCode.Validation, error.Code);
    }
}
