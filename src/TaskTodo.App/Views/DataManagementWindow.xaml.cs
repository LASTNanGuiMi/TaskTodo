using System.Windows;
using TaskTodo.App.ViewModels;

namespace TaskTodo.App.Views;

public partial class DataManagementWindow : Window
{
    private readonly DataManagementViewModel _viewModel;

    public DataManagementWindow(DataManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Loaded += async (_, _) => await viewModel.RefreshAsync();
        Closing += (_, e) => e.Cancel = viewModel.IsBusy;
    }

    private async void Backup_OnClick(object sender, RoutedEventArgs e) => await _viewModel.BackupAsync();
    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private async void RestoreTask_OnClick(object sender, RoutedEventArgs e)
        => await _viewModel.RestoreTaskAsync((DeletedTaskEntry)((FrameworkElement)sender).DataContext);

    private async void Export_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "TaskTodo JSON|*.json", FileName = $"TaskTodo-{DateTime.Now:yyyyMMdd-HHmm}.json",
            DefaultExt = ".json", AddExtension = true,
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.ExportAsync(dialog.FileName);
    }

    private void Import_OnClick(object sender, RoutedEventArgs e) => SelectImport(false);
    private void Recover_OnClick(object sender, RoutedEventArgs e) => SelectImport(true);

    private async void SelectImport(bool sqliteBackup)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = sqliteBackup ? "SQLite 备份|*.db;*.sqlite;*.sqlite3|所有文件|*.*" : "TaskTodo JSON|*.json",
            Title = sqliteBackup ? "选择 TaskTodo 备份" : "选择 TaskTodo 导出文件",
        };
        if (dialog.ShowDialog(this) != true) return;
        await _viewModel.ImportAsync(dialog.FileName, sqliteBackup, preview =>
            System.Windows.MessageBox.Show(this,
                $"文件包含 {preview.ListCount} 个清单、{preview.TaskCount} 项任务（其中 {preview.DeletedTaskCount} 项已删除）。\n\n" +
                "将替换当前全部清单和任务，并先自动备份当前数据。是否继续？",
                "确认恢复数据", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
    }
}
