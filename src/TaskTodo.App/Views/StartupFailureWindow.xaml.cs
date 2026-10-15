using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using TaskTodo.App.ViewModels;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Backup;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.App.Views;

public partial class StartupFailureWindow : Window
{
    private readonly TaskTodoStorageOptions _options;
    private bool _recovering;

    public StartupFailureWindow(string message, TaskTodoStorageOptions options)
    {
        _options = options;
        Message = message;
        DirectoryPath = Path.GetDirectoryName(options.DatabasePath)!;
        InitializeComponent();
        DataContext = this;
    }

    public string Message { get; }
    public string DirectoryPath { get; }

    private void Retry_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OpenDirectory_OnClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { DirectoryPath }, UseShellExecute = true });

    private async void RestoreBackup_OnClick(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 TaskTodo 数据库备份",
            Filter = "TaskTodo 数据库备份 (*.db)|*.db|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(_options.BackupDirectory) ? _options.BackupDirectory : DirectoryPath,
        };
        if (picker.ShowDialog(this) != true) return;

        _recovering = true;
        System.Windows.Application.Current.SessionEnding += Recovery_OnSessionEnding;
        Actions.IsEnabled = false;
        RecoveryStatus.Text = "正在检查备份…";
        try
        {
            var preview = await SqliteRecoveryService.ReadBackupAsync(picker.FileName);
            if (System.Windows.MessageBox.Show(this,
                $"备份包含 {preview.ListCount} 个清单、{preview.TaskCount} 项任务（其中已完成 {preview.CompletedTaskCount} 项、最近删除 {preview.DeletedTaskCount} 项）。\n\n恢复将替换当前数据。原数据库及关联文件会保留到数据目录中的 RecoveryOriginal 文件夹，窗口位置会重新计算。\n\n确定恢复？",
                "确认恢复备份", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                RecoveryStatus.Text = "已取消恢复，当前数据未改变。";
                return;
            }
            RecoveryStatus.Text = "正在恢复备份…";
            await SqliteRecoveryService.RestoreAsync(_options, picker.FileName);
            _recovering = false;
            DialogResult = true;
        }
        catch (TaskTodoException error)
        {
            RecoveryStatus.Text = error.Code == TaskTodoErrorCode.Validation
                ? "所选备份中的任务或清单数据无效，请选择其他备份。"
                : OperationError.Describe(error);
        }
        finally
        {
            System.Windows.Application.Current.SessionEnding -= Recovery_OnSessionEnding;
            _recovering = false;
            Actions.IsEnabled = true;
        }
    }

    private void Recovery_OnSessionEnding(object sender, SessionEndingCancelEventArgs e) => e.Cancel = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_recovering) e.Cancel = true;
        base.OnClosing(e);
    }
}
