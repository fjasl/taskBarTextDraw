using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarTextOverlayWpf
{
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;

        public TrayIcon(Action onOpenSettings, Action onToggleOverlay, Action onExit)
        {
            _icon = new NotifyIcon
            {
                Text = "Taskbar Text Overlay",
                Icon = SystemIcons.Application,
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            _icon.ContextMenuStrip.Items.Add("设置(&S)...", null, (_, __) => onOpenSettings());
            _icon.ContextMenuStrip.Items.Add("显示/隐藏文本(&T)", null, (_, __) => onToggleOverlay());
            _icon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _icon.ContextMenuStrip.Items.Add("退出(&X)", null, (_, __) => onExit());

            _icon.DoubleClick += (_, __) => onOpenSettings();
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
