using System.Drawing;
using System.Windows.Forms;

namespace TaskTodo.App.Desktop;

internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private bool _disposed;

    public TrayService(Action showOrRecover, Action dock, Action hide, Action exit)
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add(CreateItem("显示/找回", showOrRecover, isDefault: true));
        _menu.Items.Add(CreateItem("收起到边缘", dock));
        _menu.Items.Add(CreateItem("隐藏", hide));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateItem("退出", exit));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = LoadIcon(),
            Text = "TaskTodo",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(showOrRecover);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    // The tray wants the small-icon size for the current DPI; fall back to the stock icon if the resource is missing.
    private static Icon LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/TaskTodo;component/Assets/TaskTodo.ico"));
            if (resource is not null)
                using (resource.Stream)
                    return new Icon(resource.Stream, SystemInformation.SmallIconSize);
        }
        catch (System.IO.IOException) { }
        return SystemIcons.Application;
    }

    private static ToolStripMenuItem CreateItem(string text, Action action, bool isDefault = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            Font = isDefault ? new Font(SystemFonts.MenuFont!, FontStyle.Bold) : SystemFonts.MenuFont,
        };
        item.Click += (_, _) => Dispatch(action);
        return item;
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
