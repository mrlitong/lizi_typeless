using System.Drawing;
using System.Windows.Forms;

namespace lizi_typeless.Windows;

internal sealed class TrayIconHost : IDisposable
{
    private readonly ToolStripMenuItem _statusItem;
    private readonly NotifyIcon _notifyIcon;

    public TrayIconHost(Action showHistory, Action exit)
    {
        _statusItem = new ToolStripMenuItem("推理服务：检查中") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("打开历史记录", image: null, (_, _) => showHistory());
        menu.Items.Add("退出", image: null, (_, _) => exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "lizi_typeless",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => showHistory();
    }

    public void SetServiceStatus(string status) => _statusItem.Text = $"推理服务：{status}";

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
