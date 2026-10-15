using System.Windows.Automation;
using Microsoft.Data.Sqlite;
using TaskTodo.App.Tests.Support;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

[Collection("Desktop")]
public sealed class MultiListProcessTests
{
    [Fact]
    public async Task Settings_create_select_and_rename_a_list_and_open_data_management()
    {
        using var storage = new TemporaryTaskTodoStorage();
        using var process = DesktopProcessProbe.Start(storage.RootPath);
        try
        {
            var main = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo");
            DesktopProcessProbe.Invoke(await DesktopProcessProbe.WaitForEnabledById(main, "Settings"));
            await InvokeVisibleButton(main, "CreateList");
            await SaveListName("新建清单", "桌面测试清单");
            await DesktopProcessProbe.WaitFor(() => FindListId(storage, "桌面测试清单") is not null);
            var id = FindListId(storage, "桌面测试清单");
            var picker = (SelectionPattern)(await DesktopProcessProbe.WaitForEnabledById(main, "ListPicker"))
                .GetCurrentPattern(SelectionPattern.Pattern);
            Assert.Equal("桌面测试清单", Assert.Single(picker.Current.GetSelection()).Current.Name);

            await InvokeVisibleButton(main, "RenameList");
            await SaveListName("重命名清单", "已重命名的清单");
            await DesktopProcessProbe.WaitFor(() => FindListId(storage, "已重命名的清单") == id);
            Assert.Null(FindListId(storage, "桌面测试清单"));
            Assert.Equal("已重命名的清单", Assert.Single(picker.Current.GetSelection()).Current.Name);

            await InvokeVisibleButton(main, "DataTools");
            var dataTools = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo — 数据管理");
            var close = dataTools.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, "关闭")));
            await DesktopProcessProbe.WaitFor(() => close.Current.IsEnabled);
            DesktopProcessProbe.Invoke(close);
            await DesktopProcessProbe.WaitFor(() => main.Current.IsEnabled);

            async Task SaveListName(string title, string name)
            {
                var dialog = await DesktopProcessProbe.WaitForWindow(process, title);
                ((ValuePattern)DesktopProcessProbe.ById(dialog, "ListNameInput").GetCurrentPattern(ValuePattern.Pattern)).SetValue(name);
                DesktopProcessProbe.Invoke(DesktopProcessProbe.ById(dialog, "SaveList"));
                await DesktopProcessProbe.WaitFor(() => main.Current.IsEnabled);
                main = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo");
            }
        }
        finally { await DesktopProcessProbe.Stop(process); }
    }

    private static async Task InvokeVisibleButton(AutomationElement window, string id)
    {
        var button = DesktopProcessProbe.ById(window, id);
        if (button.Current.IsOffscreen)
        {
            if (button.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scrollItem))
                ((ScrollItemPattern)scrollItem).ScrollIntoView();
            else
            {
                var ancestor = TreeWalker.ControlViewWalker.GetParent(button);
                while (ancestor is not null)
                {
                    if (ancestor.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrolling))
                    {
                        ((ScrollPattern)scrolling).SetScrollPercent(ScrollPattern.NoScroll, 100);
                        break;
                    }
                    ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor);
                }
            }
            await DesktopProcessProbe.WaitFor(() => !button.Current.IsOffscreen);
        }
        await DesktopProcessProbe.WaitFor(() => button.Current.IsEnabled);
        DesktopProcessProbe.Invoke(button);
    }

    private static string? FindListId(TemporaryTaskTodoStorage storage, string name)
    {
        using var connection = storage.OpenConnection(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM TaskLists WHERE Name = $name;";
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() as string;
    }
}
