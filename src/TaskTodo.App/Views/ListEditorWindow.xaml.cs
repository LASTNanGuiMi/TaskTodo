using System.Windows;
using TaskTodo.App.ViewModels;

namespace TaskTodo.App.Views;

public partial class ListEditorWindow : Window
{
    private readonly Func<string, Task<bool>> _save;
    public ListEditorWindow(string title, string name, Func<string, Task<bool>> save, MainWindowViewModel owner)
    {
        _save = save;
        InitializeComponent();
        Title = title;
        DataContext = owner;
        NameInput.Text = name;
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
        Closing += (_, e) => e.Cancel = owner.IsSaving;
    }

    private async void Save_OnClick(object sender, RoutedEventArgs e) => await SaveAsync();
    private async void Save_OnSubmit(object? sender, EventArgs e) => await SaveAsync();
    private async Task SaveAsync()
    {
        if (!NameInput.IsComposing && await _save(NameInput.Text)) DialogResult = true;
    }
}
