using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using Microsoft.Data.Sqlite;
using TaskTodo.App.Tests.Support;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

[Collection("Desktop")]
public sealed class AppProcessTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Actual_entry_point_adds_docks_reopens_and_keeps_committed_data_after_process_exit()
    {
        using var storage = new TemporaryTaskTodoStorage();
        using (var process = Start(storage))
        {
            try
            {
                var main = await WaitForWindow(process, "TaskTodo");
                var input = await DesktopProcessProbe.WaitForEnabledById(main, "QuickAdd");
                ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue("独立程序持久化检查");
                var add = await DesktopProcessProbe.WaitForEnabledById(main, "AddTask");
                ((InvokePattern)add.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                await WaitFor(() => CountTasks(storage) == 1);
                var check = await DesktopProcessProbe.WaitForEnabledControl(main,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox), "active task checkbox");
                ((TogglePattern)check.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
                await WaitFor(() => CountCompleted(storage) == 1);
                var completed = await DesktopProcessProbe.WaitForEnabledById(main, "CompletedTasks");
                ((ExpandCollapsePattern)completed.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                check = await DesktopProcessProbe.WaitForEnabledControl(completed,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox), "completed task checkbox");
                ((TogglePattern)check.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
                await WaitFor(() => CountCompleted(storage) == 0);
                var dockButton = await DesktopProcessProbe.WaitForEnabledById(main, "DockToEdge");
                ((InvokePattern)dockButton.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                var capsule = await WaitForWindow(process, "TaskTodo 胶囊");
                // The capsule appears before the dock transition has persisted its state; click only once it has.
                await WaitFor(() => ReadStartupState(storage) == "Docked");
                var open = await DesktopProcessProbe.WaitForEnabledControl(capsule,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), "capsule open button");
                ((InvokePattern)open.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                await WaitForWindow(process, "TaskTodo");
                await WaitFor(() => ReadStartupState(storage) == "Expanded");
                Assert.Equal(1, CountTasks(storage));
            }
            catch (Exception error)
            {
                output.WriteLine(error.ToString());
                throw;
            }
            finally { await StopTestProcess(process); }
        }

        using var restarted = Start(storage);
        try
        {
            var main = await WaitForWindow(restarted, "TaskTodo");
            await WaitFor(() => main.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "独立程序持久化检查")) is not null);
            Assert.Equal(1, CountTasks(storage));
            Assert.True(restarted.CloseMainWindow());
            await WaitForWindow(restarted, "TaskTodo 胶囊"); // Closing the panel collapses it to the edge.
            Assert.False(restarted.HasExited);
        }
        catch (Exception error)
        {
            output.WriteLine(error.ToString());
            throw;
        }
        finally { await StopTestProcess(restarted); }
    }

    [Fact]
    public async Task Corrupt_database_shows_recovery_actions_and_preserves_the_original_file()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var original = Encoding.UTF8.GetBytes("TaskTodo isolated corrupt database test");
        await File.WriteAllBytesAsync(storage.Options.DatabasePath, original);
        using var process = Start(storage);
        try
        {
            var dialog = await WaitForWindow(process, "TaskTodo — 无法打开数据");
            Assert.NotNull(dialog.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "重试")));
            Assert.NotNull(dialog.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "打开数据目录")));
            var exit = await DesktopProcessProbe.WaitForEnabledControl(dialog,
                new PropertyCondition(AutomationElement.NameProperty, "退出"), "startup exit button");
            ((InvokePattern)exit.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(original, await File.ReadAllBytesAsync(storage.Options.DatabasePath));
        }
        finally { await StopTestProcess(process); }
    }

    private static Process Start(TemporaryTaskTodoStorage storage)
        => DesktopProcessProbe.Start(storage.RootPath);

    private static Task<AutomationElement> WaitForWindow(Process process, string title)
        => DesktopProcessProbe.WaitForWindow(process, title);

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail("The application did not reach the expected UI/database state within five seconds.");
    }

    private static long CountTasks(TemporaryTaskTodoStorage storage)
    {
        using var connection = storage.OpenConnection(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TodoTasks WHERE DeletedAtUtc IS NULL;";
        return (long)command.ExecuteScalar()!;
    }

    private static string? ReadStartupState(TemporaryTaskTodoStorage storage)
    {
        using var connection = storage.OpenConnection(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json_extract(JsonValue, '$.lastPresentationState') FROM Settings WHERE Key = 'application';";
        return command.ExecuteScalar() as string;
    }

    private static long CountCompleted(TemporaryTaskTodoStorage storage)
    {
        using var connection = storage.OpenConnection(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TodoTasks WHERE IsCompleted = 1 AND DeletedAtUtc IS NULL;";
        return (long)command.ExecuteScalar()!;
    }

    private static Task StopTestProcess(Process process) => DesktopProcessProbe.Stop(process);
}
