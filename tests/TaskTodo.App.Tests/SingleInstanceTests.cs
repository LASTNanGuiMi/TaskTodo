using TaskTodo.App.Desktop;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public async Task Same_directory_aliases_activate_one_primary_and_repeated_launches_keep_listening()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var activations = 0;
        await using var primary = await SingleInstanceService.StartAsync(storage.RootPath,
            () => Interlocked.Increment(ref activations));
        Assert.NotNull(primary);
        Assert.False(primary.Completion.IsCompleted);

        foreach (var path in new[] { storage.RootPath, storage.RootPath.ToUpperInvariant(), Path.Combine(storage.RootPath, ".") })
        {
            await using var secondary = await SingleInstanceService.StartAsync(path,
                () => Assert.Fail("A secondary must not listen."));
            Assert.Null(secondary);
        }
        Assert.Equal(3, activations);
    }

    [Fact]
    public async Task Independent_data_directories_run_concurrently_and_disposal_allows_restart()
    {
        using var firstStorage = new TemporaryTaskTodoStorage();
        using var secondStorage = new TemporaryTaskTodoStorage();
        var first = await SingleInstanceService.StartAsync(firstStorage.RootPath, () => { });
        await using var second = await SingleInstanceService.StartAsync(secondStorage.RootPath, () => { });
        Assert.NotNull(first);
        Assert.NotNull(second);
        await first.DisposeAsync();
        Assert.True(first.Completion.IsCompletedSuccessfully);
        await using var replacement = await SingleInstanceService.StartAsync(firstStorage.RootPath, () => { });
        Assert.NotNull(replacement);
    }

    [Fact]
    public async Task Listener_failure_is_observable_and_cleanup_releases_the_instance_without_throwing_again()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var primary = await SingleInstanceService.StartAsync(storage.RootPath,
            () => throw new IOException("Injected activation failure."));
        Assert.NotNull(primary);
        await Assert.ThrowsAsync<EndOfStreamException>(() => SingleInstanceService.StartAsync(storage.RootPath, () => { }));
        await Assert.ThrowsAsync<IOException>(() => primary.Completion);
        await primary.DisposeAsync();
        await using var replacement = await SingleInstanceService.StartAsync(storage.RootPath, () => { });
        Assert.NotNull(replacement);
    }
}
