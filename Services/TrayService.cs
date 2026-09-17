using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Services;

public class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Window _mainWindow;
    private readonly ToolStripMenuItem _titleItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _pauseItem;

    public event Action? StartRequested;
    public event Action? PauseRequested;
    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService(Window mainWindow)
    {
        _mainWindow = mainWindow;
        _notifyIcon = new NotifyIcon();

        var contextMenu = new ContextMenuStrip();

        _titleItem = new ToolStripMenuItem("Antigravity Auto Pilot") { Enabled = false, Font = new Font(Control.DefaultFont, System.Drawing.FontStyle.Bold) };
        contextMenu.Items.Add(_titleItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        _startItem = new ToolStripMenuItem("Bắt đầu", null, (s, e) => StartRequested?.Invoke());
        _pauseItem = new ToolStripMenuItem("Tạm dừng", null, (s, e) => PauseRequested?.Invoke());
        contextMenu.Items.Add(_startItem);
        contextMenu.Items.Add(_pauseItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var openItem = new ToolStripMenuItem("Mở giao diện", null, (s, e) => OpenWindow());
        var exitItem = new ToolStripMenuItem("Thoát ứng dụng", null, (s, e) => ExitRequested?.Invoke());
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.Text = "Antigravity Auto Pilot";
        _notifyIcon.DoubleClick += (s, e) => OpenWindow();

        UpdateIcon(EngineStatus.Stopped, AntigravityConnectionState.Disconnected);
        _notifyIcon.Visible = true;
    }

    public void UpdateStatus(EngineStatus engineStatus, AntigravityConnectionState connState)
    {
        UpdateIcon(engineStatus, connState);

        string statusText = engineStatus switch
        {
            EngineStatus.Running => "ĐANG CHẠY",
            EngineStatus.Paused => "TẠM DỪNG",
            _ => "ĐÃ DỪNG"
        };

        string connText = connState == AntigravityConnectionState.Connected ? "Đã kết nối IDE" : "Đang chờ IDE";
        _notifyIcon.Text = $"Antigravity Auto Pilot ({statusText} - {connText})";

        _startItem.Enabled = engineStatus != EngineStatus.Running;
        _pauseItem.Enabled = engineStatus == EngineStatus.Running;
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    private void OpenWindow()
    {
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Show();
        _mainWindow.Activate();
        OpenRequested?.Invoke();
    }

    private void UpdateIcon(EngineStatus engineStatus, AntigravityConnectionState connState)
    {
        Color dotColor;
        if (connState == AntigravityConnectionState.Disconnected)
        {
            dotColor = Color.FromArgb(120, 120, 120); // Gray
        }
        else if (engineStatus == EngineStatus.Running)
        {
            dotColor = Color.FromArgb(34, 197, 94); // Green
        }
        else if (engineStatus == EngineStatus.Paused)
        {
            dotColor = Color.FromArgb(234, 179, 8); // Amber
        }
        else
        {
            dotColor = Color.FromArgb(56, 189, 248); // Blue
        }

        try
        {
            using var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Draw sleek circular badge with inner dot
                using var bgBrush = new SolidBrush(Color.FromArgb(24, 24, 27));
                g.FillEllipse(bgBrush, 0, 0, 15, 15);

                using var ringPen = new Pen(Color.FromArgb(63, 63, 70), 1);
                g.DrawEllipse(ringPen, 0, 0, 15, 15);

                using var dotBrush = new SolidBrush(dotColor);
                g.FillEllipse(dotBrush, 4, 4, 7, 7);
            }

            var hIcon = bitmap.GetHicon();
            _notifyIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
