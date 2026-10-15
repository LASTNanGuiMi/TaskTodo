using System.Text;
using System.Windows.Automation;
using TaskTodo.App.Tests.Support;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

[Collection("Desktop")]
public sealed class StartupRecoveryProcessTests
{
    [Fact]
    public async Task User_can_preview_and_confirm_a_backup_to_recover_a_corrupt_database()
    {
        using var storage = new TemporaryTaskTodoStorage();
        string backupPath;
        await using (var service = await storage.OpenServiceAsync())
        {
            var snapshot = await service.GetSnapshotAsync();
            await service.CreateTaskAsync(Guid.NewGuid(), snapshot.Lists[0].Id, "从启动恢复入口找回的任务");
            backupPath = await service.CreateBackupAsync();
        }
        var corruptBytes = Encoding.UTF8.GetBytes("TaskTodo recovery UI isolated corrupt database");
        await File.WriteAllBytesAsync(storage.Options.DatabasePath, corruptBytes);

        using var process = DesktopProcessProbe.Start(storage.RootPath);
        try
        {
            var failure = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo — 无法打开数据");
            DesktopProcessProbe.Invoke(await DesktopProcessProbe.WaitForEnabledById(failure, "RestoreStartupBackup"));
            var picker = await DesktopProcessProbe.WaitForWindow(process, "选择 TaskTodo 数据库备份");
            var fileName = await DesktopProcessProbe.WaitForEnabledById(picker, "1148");
            ((ValuePattern)fileName.GetCurrentPattern(ValuePattern.Pattern)).SetValue(backupPath);
            DesktopProcessProbe.Invoke(await DesktopProcessProbe.WaitForEnabledById(picker, "1"));
            var confirmation = await DesktopProcessProbe.WaitForWindow(process, "确认恢复备份");
            Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(storage.Options.DatabasePath));
            DesktopProcessProbe.Invoke(await DesktopProcessProbe.WaitForEnabledById(confirmation, "6"));

            var main = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo");
            await DesktopProcessProbe.WaitFor(() => main.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "从启动恢复入口找回的任务")) is not null);
            var preserved = Directory.GetDirectories(storage.RootPath, "RecoveryOriginal-*");
            Assert.Single(preserved);
            Assert.Equal(corruptBytes,
                await File.ReadAllBytesAsync(Path.Combine(preserved[0], "tasktodo.db")));
        }
        finally { await DesktopProcessProbe.Stop(process); }
    }
}
