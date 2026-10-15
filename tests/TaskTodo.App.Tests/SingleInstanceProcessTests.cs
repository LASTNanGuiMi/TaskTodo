using TaskTodo.App.Tests.Support;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

[Collection("Desktop")]
public sealed class SingleInstanceProcessTests
{
    [Fact]
    public async Task Concurrent_startup_keeps_exactly_one_business_process()
    {
        using var storage = new TemporaryTaskTodoStorage();
        using var first = DesktopProcessProbe.Start(storage.RootPath);
        using var second = DesktopProcessProbe.Start(storage.RootPath);
        try
        {
            await Task.WhenAny(first.WaitForExitAsync(), second.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotEqual(first.HasExited, second.HasExited);
            var secondary = first.HasExited ? first : second;
            var primary = first.HasExited ? second : first;
            Assert.Equal(0, secondary.ExitCode);
            await DesktopProcessProbe.WaitForWindow(primary, "TaskTodo");
        }
        finally
        {
            await DesktopProcessProbe.Stop(first);
            await DesktopProcessProbe.Stop(second);
        }
    }

    [Fact]
    public async Task Second_process_exits_and_recovers_the_hidden_first_window()
    {
        using var storage = new TemporaryTaskTodoStorage();
        using var primary = DesktopProcessProbe.Start(storage.RootPath);
        try
        {
            await DesktopProcessProbe.WaitForWindow(primary, "TaskTodo");
            primary.Refresh();
            Assert.True(primary.CloseMainWindow());
            await Task.Delay(200);
            Assert.False(primary.HasExited);

            using var secondary = DesktopProcessProbe.Start(Path.Combine(storage.RootPath, "."));
            await secondary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, secondary.ExitCode);
            await DesktopProcessProbe.WaitForWindow(primary, "TaskTodo");
            Assert.False(primary.HasExited);
        }
        finally { await DesktopProcessProbe.Stop(primary); }
    }
}
