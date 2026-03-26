using System.Drawing;
using System.Windows.Forms;

namespace Yoink.UI;

/// <summary>
/// System tray icon with context menu for Yoink.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? OnSettings;
    public event Action? OnQuit;

    public TrayIcon()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Settings", null, (_, _) => OnSettings?.Invoke());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Quit Yoink", null, (_, _) => OnQuit?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Text = "Yoink - Screenshot Tool",
            Icon = CreateDefaultIcon(),
            ContextMenuStrip = contextMenu,
            Visible = true
        };
    }

    /// <summary>
    /// Creates a simple default icon programmatically.
    /// This avoids needing an .ico file for the initial build.
    /// </summary>
    private static Icon CreateDefaultIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(0, 0, 0, 0));

        // Draw a simple "Y" shape in white
        using var pen = new Pen(Color.White, 3f);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.DrawLine(pen, 6, 4, 16, 16);
        g.DrawLine(pen, 26, 4, 16, 16);
        g.DrawLine(pen, 16, 16, 16, 28);

        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(2000, title, text, icon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
