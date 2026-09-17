using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AntigravityAutoPilot.Core;
using AntigravityAutoPilot.Models;
using AntigravityAutoPilot.Services;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Application = System.Windows.Application;

namespace AntigravityAutoPilot.UI;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly AutomationScanner _scanner;
    private readonly HotkeyService _hotkeyService;
    private readonly TrayService _trayService;
    private readonly AudioService _audioService;

    private AppSettings _settings;
    private bool _isExplicitExit = false;
    private bool _isInitializing = true;

    public MainWindow()
    {
        InitializeComponent();

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        _logService = new LogService();
        _scanner = new AutomationScanner(_settings);
        _hotkeyService = new HotkeyService();
        _trayService = new TrayService(this);
        _audioService = new AudioService();

        LogListBox.ItemsSource = _logService.Entries;

        BindScannerEvents();
        BindHotkeyAndTrayEvents();
        ApplySettingsToUI();

        _isInitializing = false;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hotkeyService.Register(this);

        _logService.LogInfo("Khởi tạo Antigravity Auto Pilot thành công.");
        _logService.LogInfo("Phím tắt toàn cục: Ctrl+Shift+F11 (Bắt đầu), Ctrl+Shift+F12 (Tạm dừng khẩn cấp).");

        if (_settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            Hide();
        }

        // Start scanning automatically on launch
        StartAutomation();
    }

    private void BindScannerEvents()
    {
        _scanner.StatusChanged += status => Dispatcher.Invoke(() =>
        {
            UpdateEngineStatusUI(status);
            _trayService.UpdateStatus(status, _scanner.Status == EngineStatus.Running ? AntigravityConnectionState.Connected : AntigravityConnectionState.Disconnected);
        });

        _scanner.AntigravityStateChanged += (state, message) => Dispatcher.Invoke(() =>
        {
            UpdateConnectionUI(state, message);
            _logService.Log(message, state == AntigravityConnectionState.Connected ? LogLevel.Success : LogLevel.Warning);
            _trayService.UpdateStatus(_scanner.Status, state);
        });

        _scanner.ActionExecuted += (action, method) => Dispatcher.Invoke(() =>
        {
            TxtActionsCount.Text = _scanner.TotalActionsClicked.ToString();
            TxtLatestEvent.Text = $"Đã click: {action.ButtonText}";
            _logService.LogSuccess($"Đã click {action.ButtonText.ToUpperInvariant()} ({method})");
            ScrollLogToEnd();
        });

        _scanner.ActionBlocked += (action, reason) => Dispatcher.Invoke(() =>
        {
            TxtBlockedCount.Text = _scanner.TotalBlocked.ToString();
            TxtLatestEvent.Text = $"ĐÃ CHẶN: {action.DetectedCommand ?? action.ButtonText}";
            _logService.LogDanger($"ĐÃ CHẶN: {action.ButtonText} — {reason}");

            // Show tray warning
            _trayService.ShowNotification(
                "Đã Chặn Lệnh Nguy Hiểm",
                $"Auto Pilot đã chặn hành động nguy hiểm: {action.DetectedCommand ?? action.ButtonText}",
                System.Windows.Forms.ToolTipIcon.Warning);

            ScrollLogToEnd();
        });

        _scanner.SubmitApproved += (action) => Dispatcher.Invoke(() =>
        {
            if (_settings.EnableSubmitSoundNotification)
            {
                _logService.LogSuccess("Duyệt Submit thành công! Phát âm thanh submit.mp3.");
                _audioService.PlaySubmit(_settings.SubmitSoundFilePath, _settings.SoundVolumePercent);
            }
        });

        _scanner.AcceptApproved += (action) => Dispatcher.Invoke(() =>
        {
            if (_settings.EnableAcceptSoundNotification)
            {
                _logService.LogSuccess("Duyệt Accept All thành công! Phát âm thanh accept_all.mp3.");
                _audioService.PlayAccept(_settings.AcceptSoundFilePath, _settings.SoundVolumePercent);
            }
        });

        _scanner.PlanDetected += (text) => Dispatcher.Invoke(() =>
        {
            if (_settings.EnablePlanSoundNotification)
            {
                _logService.LogSuccess("Phát hiện Implementation Plan! Phát âm thanh plan.mp3.");
                _audioService.PlayPlan(_settings.PlanSoundFilePath, _settings.SoundVolumePercent);
            }
        });

        _scanner.PlanTabClosed += (tabName) => Dispatcher.Invoke(() =>
        {
            _logService.LogSuccess($"[TỰ ĐỘNG ĐÓNG TAB] Đã đóng tab '{tabName}' sau khi Proceed (không xóa file).");
            TxtLatestEvent.Text = $"Đã đóng tab: {tabName}";
            ScrollLogToEnd();
        });

        _scanner.DiagnosticLogged += (msg) => Dispatcher.Invoke(() =>
        {
            _logService.LogInfo(msg);
        });

        _scanner.AntigravityCompleted += () => Dispatcher.Invoke(() =>
        {
            TxtLatestEvent.Text = "Antigravity đã hoàn thành tác vụ";
            _logService.LogSuccess("Antigravity đã chạy xong! Phát âm thanh thông báo done.mp3.");

            _trayService.ShowNotification(
                "Antigravity Hoàn Tất",
                "Antigravity IDE đã hoàn thành tác vụ!",
                System.Windows.Forms.ToolTipIcon.Info);

            if (_settings.EnableSoundNotification)
            {
                _audioService.PlayDone(_settings.SoundFilePath, _settings.SoundVolumePercent);
            }

            ScrollLogToEnd();
        });
    }

    private void BindHotkeyAndTrayEvents()
    {
        _hotkeyService.PauseRequested += () => Dispatcher.Invoke(PauseAutomation);
        _hotkeyService.ResumeRequested += () => Dispatcher.Invoke(StartAutomation);

        _trayService.StartRequested += () => Dispatcher.Invoke(StartAutomation);
        _trayService.PauseRequested += () => Dispatcher.Invoke(PauseAutomation);
        _trayService.ExitRequested += () => Dispatcher.Invoke(ShutdownApplication);
    }

    private void UpdateEngineStatusUI(EngineStatus status)
    {
        switch (status)
        {
            case EngineStatus.Running:
                EngineStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22c55e"));
                EngineStatusText.Text = "ĐANG CHẠY";
                EngineStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22c55e"));
                EngineStatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803d"));
                BtnStart.IsEnabled = false;
                BtnPause.IsEnabled = true;
                break;
            case EngineStatus.Paused:
                EngineStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b"));
                EngineStatusText.Text = "TẠM DỪNG";
                EngineStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b"));
                EngineStatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#b45309"));
                BtnStart.IsEnabled = true;
                BtnPause.IsEnabled = false;
                break;
            default:
                EngineStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9ca3af"));
                EngineStatusText.Text = "ĐÃ DỪNG";
                EngineStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9ca3af"));
                EngineStatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151"));
                BtnStart.IsEnabled = true;
                BtnPause.IsEnabled = false;
                break;
        }
    }

    private void UpdateConnectionUI(AntigravityConnectionState state, string message)
    {
        if (state == AntigravityConnectionState.Connected)
        {
            ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22c55e"));
            ConnectionText.Text = "ĐÃ KẾT NỐI";
            ConnectionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22c55e"));
            ConnectionBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803d"));
        }
        else
        {
            ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ef4444"));
            ConnectionText.Text = "Đang chờ Antigravity...";
            ConnectionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ef4444"));
            ConnectionBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7f1d1d"));
        }
    }

    private void ApplySettingsToUI()
    {
        // Whitelist toggles
        ChkAccept.IsChecked = _settings.EnableAccept;
        ChkAcceptAll.IsChecked = _settings.EnableAcceptAll;
        ChkContinue.IsChecked = _settings.EnableContinue;
        ChkRun.IsChecked = _settings.EnableRun;
        ChkSubmit.IsChecked = _settings.EnableSubmit;
        ChkRetry.IsChecked = _settings.EnableRetry;
        ChkAllow.IsChecked = _settings.EnableAllow;
        ChkApply.IsChecked = _settings.EnableApply;
        ChkConfirm.IsChecked = _settings.EnableConfirm;
        ChkProceed.IsChecked = _settings.EnableProceed;
        ChkAutoAnswer.IsChecked = _settings.EnableAutoAnswerQuestion;
        ChkAutoCloseProceededPlans.IsChecked = _settings.AutoCloseProceededPlans;
        ChkAutoCloseProceededPlansSettings.IsChecked = _settings.AutoCloseProceededPlans;

        // Operating Mode
        switch (_settings.Mode)
        {
            case OperatingMode.Safe:
                RbSafe.IsChecked = true;
                break;
            case OperatingMode.FullAuto:
                RbFullAuto.IsChecked = true;
                break;
            default:
                RbNormal.IsChecked = true;
                break;
        }

        // Timing
        SliderScanInterval.Value = _settings.ScanIntervalMs;
        TxtScanIntervalVal.Text = $"{_settings.ScanIntervalMs} ms";

        SliderDebounce.Value = _settings.DebounceMs;
        TxtDebounceVal.Text = $"{_settings.DebounceMs} ms";

        // General
        ChkPhysicalClickFallback.IsChecked = _settings.AllowPhysicalClickFallback;
        ChkMinimizeToTray.IsChecked = _settings.MinimizeToTrayOnClose;
        ChkStartMinimized.IsChecked = _settings.StartMinimized;

        // Sound Notifications
        ChkSoundNotification.IsChecked = _settings.EnableSoundNotification;
        ChkSoundNotificationSettings.IsChecked = _settings.EnableSoundNotification;
        TxtSoundFilePath.Text = _settings.SoundFilePath;

        ChkAcceptSoundNotification.IsChecked = _settings.EnableAcceptSoundNotification;
        ChkAcceptSoundNotificationSettings.IsChecked = _settings.EnableAcceptSoundNotification;
        TxtAcceptSoundFilePath.Text = _settings.AcceptSoundFilePath;

        ChkSubmitSoundNotification.IsChecked = _settings.EnableSubmitSoundNotification;
        ChkSubmitSoundNotificationSettings.IsChecked = _settings.EnableSubmitSoundNotification;
        TxtSubmitSoundFilePath.Text = _settings.SubmitSoundFilePath;

        ChkPlanSoundNotification.IsChecked = _settings.EnablePlanSoundNotification;
        ChkPlanSoundNotificationSettings.IsChecked = _settings.EnablePlanSoundNotification;
        TxtPlanSoundFilePath.Text = _settings.PlanSoundFilePath;

        SliderVolume.Value = _settings.SoundVolumePercent;
        TxtVolumeVal.Text = $"{_settings.SoundVolumePercent}%";

        // Custom Lists
        RefreshCustomLists();
    }

    private void RefreshCustomLists()
    {
        ListCustomWhitelist.ItemsSource = null;
        ListCustomWhitelist.ItemsSource = _settings.CustomWhitelist;

        ListCustomBlacklist.ItemsSource = null;
        ListCustomBlacklist.ItemsSource = _settings.CustomBlacklist;

        ListDangerousCmds.ItemsSource = null;
        ListDangerousCmds.ItemsSource = _settings.DangerousCommands;
    }

    private void UpdateSettingsFromUI(object? sender = null)
    {
        if (_isInitializing) return;

        _settings.EnableAccept = ChkAccept.IsChecked == true;
        _settings.EnableAcceptAll = ChkAcceptAll.IsChecked == true;
        _settings.EnableContinue = ChkContinue.IsChecked == true;
        _settings.EnableRun = ChkRun.IsChecked == true;
        _settings.EnableSubmit = ChkSubmit.IsChecked == true;
        _settings.EnableRetry = ChkRetry.IsChecked == true;
        _settings.EnableAllow = ChkAllow.IsChecked == true;
        _settings.EnableApply = ChkApply.IsChecked == true;
        _settings.EnableConfirm = ChkConfirm.IsChecked == true;
        _settings.EnableProceed = ChkProceed.IsChecked == true;
        _settings.EnableAutoAnswerQuestion = ChkAutoAnswer.IsChecked == true;

        if (RbSafe.IsChecked == true) _settings.Mode = OperatingMode.Safe;
        else if (RbFullAuto.IsChecked == true) _settings.Mode = OperatingMode.FullAuto;
        else _settings.Mode = OperatingMode.Normal;

        _settings.ScanIntervalMs = (int)SliderScanInterval.Value;
        _settings.DebounceMs = (int)SliderDebounce.Value;

        _settings.AllowPhysicalClickFallback = ChkPhysicalClickFallback.IsChecked == true;
        _settings.MinimizeToTrayOnClose = ChkMinimizeToTray.IsChecked == true;
        _settings.StartMinimized = ChkStartMinimized.IsChecked == true;

        // Sync Dashboard and Settings checkboxes if one was clicked
        if (sender == ChkSoundNotification && ChkSoundNotificationSettings != null && ChkSoundNotification != null)
            ChkSoundNotificationSettings.IsChecked = ChkSoundNotification.IsChecked;
        else if (sender == ChkSoundNotificationSettings && ChkSoundNotification != null && ChkSoundNotificationSettings != null)
            ChkSoundNotification.IsChecked = ChkSoundNotificationSettings.IsChecked;

        if (sender == ChkAcceptSoundNotification && ChkAcceptSoundNotificationSettings != null && ChkAcceptSoundNotification != null)
            ChkAcceptSoundNotificationSettings.IsChecked = ChkAcceptSoundNotification.IsChecked;
        else if (sender == ChkAcceptSoundNotificationSettings && ChkAcceptSoundNotification != null && ChkAcceptSoundNotificationSettings != null)
            ChkAcceptSoundNotification.IsChecked = ChkAcceptSoundNotificationSettings.IsChecked;

        if (sender == ChkSubmitSoundNotification && ChkSubmitSoundNotificationSettings != null && ChkSubmitSoundNotification != null)
            ChkSubmitSoundNotificationSettings.IsChecked = ChkSubmitSoundNotification.IsChecked;
        else if (sender == ChkSubmitSoundNotificationSettings && ChkSubmitSoundNotification != null && ChkSubmitSoundNotificationSettings != null)
            ChkSubmitSoundNotification.IsChecked = ChkSubmitSoundNotificationSettings.IsChecked;

        if (sender == ChkPlanSoundNotification && ChkPlanSoundNotificationSettings != null && ChkPlanSoundNotification != null)
            ChkPlanSoundNotificationSettings.IsChecked = ChkPlanSoundNotification.IsChecked;
        else if (sender == ChkPlanSoundNotificationSettings && ChkPlanSoundNotification != null && ChkPlanSoundNotificationSettings != null)
            ChkPlanSoundNotification.IsChecked = ChkPlanSoundNotificationSettings.IsChecked;

        if (sender == ChkAutoCloseProceededPlans && ChkAutoCloseProceededPlansSettings != null && ChkAutoCloseProceededPlans != null)
            ChkAutoCloseProceededPlansSettings.IsChecked = ChkAutoCloseProceededPlans.IsChecked;
        else if (sender == ChkAutoCloseProceededPlansSettings && ChkAutoCloseProceededPlans != null && ChkAutoCloseProceededPlansSettings != null)
            ChkAutoCloseProceededPlans.IsChecked = ChkAutoCloseProceededPlansSettings.IsChecked;

        _settings.AutoCloseProceededPlans = ChkAutoCloseProceededPlans?.IsChecked == true;

        _settings.EnableSoundNotification = ChkSoundNotification?.IsChecked == true;
        _settings.SoundFilePath = TxtSoundFilePath != null && !string.IsNullOrWhiteSpace(TxtSoundFilePath.Text) ? TxtSoundFilePath.Text.Trim() : "Sounds/done.mp3";

        _settings.EnableAcceptSoundNotification = ChkAcceptSoundNotification?.IsChecked == true;
        _settings.AcceptSoundFilePath = TxtAcceptSoundFilePath != null && !string.IsNullOrWhiteSpace(TxtAcceptSoundFilePath.Text) ? TxtAcceptSoundFilePath.Text.Trim() : "Sounds/accept_all.mp3";

        _settings.EnableSubmitSoundNotification = ChkSubmitSoundNotification?.IsChecked == true;
        _settings.SubmitSoundFilePath = TxtSubmitSoundFilePath != null && !string.IsNullOrWhiteSpace(TxtSubmitSoundFilePath.Text) ? TxtSubmitSoundFilePath.Text.Trim() : "Sounds/submit.mp3";

        _settings.EnablePlanSoundNotification = ChkPlanSoundNotification?.IsChecked == true;
        _settings.PlanSoundFilePath = TxtPlanSoundFilePath != null && !string.IsNullOrWhiteSpace(TxtPlanSoundFilePath.Text) ? TxtPlanSoundFilePath.Text.Trim() : "Sounds/plan.mp3";

        _settings.SoundVolumePercent = SliderVolume != null ? (int)SliderVolume.Value : 100;

        _scanner.UpdateSettings(_settings);
    }

    #region Control Actions

    public void StartAutomation()
    {
        UpdateSettingsFromUI();
        _scanner.Start();
        _logService.LogInfo("Hệ thống quét: ĐANG CHẠY.");
    }

    public void PauseAutomation()
    {
        _scanner.Pause();
        _logService.LogWarning("Hệ thống quét: TẠM DỪNG (Kích hoạt dừng khẩn cấp).");
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        StartAutomation();
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        PauseAutomation();
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        UpdateSettingsFromUI(sender);
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettingsFromUI();
        if (!_isInitializing)
        {
            string modeName = _settings.Mode switch
            {
                OperatingMode.Safe => "An Toàn (Safe)",
                OperatingMode.FullAuto => "Tự Động Toàn Diện (Full-Auto)",
                _ => "Tiêu Chuẩn (Normal)"
            };
            _logService.LogInfo($"Đã chuyển chế độ hoạt động sang: {modeName}");
        }
    }

    private void SliderScanInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtScanIntervalVal != null)
        {
            TxtScanIntervalVal.Text = $"{(int)e.NewValue} ms";
        }
        UpdateSettingsFromUI();
    }

    private void SliderDebounce_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtDebounceVal != null)
        {
            TxtDebounceVal.Text = $"{(int)e.NewValue} ms";
        }
        UpdateSettingsFromUI();
    }

    private void BtnTestSound_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsFromUI();
        _logService.LogInfo("Đang phát thử nghiệm âm thanh done.mp3...");
        bool played = _audioService.PlayDone(_settings.SoundFilePath, _settings.SoundVolumePercent);
        if (!played)
        {
            _logService.LogWarning($"Không thể phát file âm thanh: {_settings.SoundFilePath}");
        }
    }

    private void BtnTestAcceptSound_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsFromUI();
        _logService.LogInfo("Đang phát thử nghiệm âm thanh accept_all.mp3...");
        bool played = _audioService.PlayAccept(_settings.AcceptSoundFilePath, _settings.SoundVolumePercent);
        if (!played)
        {
            _logService.LogWarning($"Không thể phát file âm thanh: {_settings.AcceptSoundFilePath}");
        }
    }

    private void BtnTestSubmitSound_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsFromUI();
        _logService.LogInfo("Đang phát thử nghiệm âm thanh submit.mp3...");
        bool played = _audioService.PlaySubmit(_settings.SubmitSoundFilePath, _settings.SoundVolumePercent);
        if (!played)
        {
            _logService.LogWarning($"Không thể phát file âm thanh: {_settings.SubmitSoundFilePath}");
        }
    }

    private void BtnTestPlanSound_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsFromUI();
        _logService.LogInfo("Đang phát thử nghiệm âm thanh plan.mp3...");
        bool played = _audioService.PlayPlan(_settings.PlanSoundFilePath, _settings.SoundVolumePercent);
        if (!played)
        {
            _logService.LogWarning($"Không thể phát file âm thanh: {_settings.PlanSoundFilePath}");
        }
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtVolumeVal != null)
        {
            TxtVolumeVal.Text = $"{(int)e.NewValue}%";
        }
        UpdateSettingsFromUI();
    }

    #endregion

    #region Activity Log Handlers

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        _logService.Clear();
    }

    private void BtnOpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = _logService.GetTodayLogFilePath();
            if (!File.Exists(path))
            {
                File.WriteAllText(path, string.Empty);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở file nhật ký: {ex.Message}", "Lỗi Nhật Ký", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScrollLogToEnd()
    {
        if (ChkAutoScroll.IsChecked == true && _logService.Entries.Count > 0)
        {
            LogListBox.ScrollIntoView(_logService.Entries.Last());
        }
    }

    #endregion

    #region Settings Tab Handlers

    private void BtnAddWhitelist_Click(object sender, RoutedEventArgs e)
    {
        string text = TxtNewWhitelist.Text.Trim();
        if (!string.IsNullOrEmpty(text) && !_settings.CustomWhitelist.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            _settings.CustomWhitelist.Add(text);
            TxtNewWhitelist.Clear();
            RefreshCustomLists();
            _settingsService.Save(_settings);
        }
    }

    private void TxtNewWhitelist_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnAddWhitelist_Click(sender, e);
    }

    private void BtnRemoveWhitelist_Click(object sender, RoutedEventArgs e)
    {
        if (ListCustomWhitelist.SelectedItem is string selected)
        {
            _settings.CustomWhitelist.Remove(selected);
            RefreshCustomLists();
            _settingsService.Save(_settings);
        }
    }

    private void BtnAddBlacklist_Click(object sender, RoutedEventArgs e)
    {
        string text = TxtNewBlacklist.Text.Trim();
        if (!string.IsNullOrEmpty(text) && !_settings.CustomBlacklist.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            _settings.CustomBlacklist.Add(text);
            TxtNewBlacklist.Clear();
            RefreshCustomLists();
            _settingsService.Save(_settings);
        }
    }

    private void TxtNewBlacklist_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnAddBlacklist_Click(sender, e);
    }

    private void BtnRemoveBlacklist_Click(object sender, RoutedEventArgs e)
    {
        if (ListCustomBlacklist.SelectedItem is string selected)
        {
            _settings.CustomBlacklist.Remove(selected);
            RefreshCustomLists();
            _settingsService.Save(_settings);
        }
    }

    private void BtnAddDangerCmd_Click(object sender, RoutedEventArgs e)
    {
        string cmd = TxtNewDangerCmd.Text.Trim();
        if (!string.IsNullOrEmpty(cmd) && !_settings.DangerousCommands.Contains(cmd, StringComparer.OrdinalIgnoreCase))
        {
            _settings.DangerousCommands.Add(cmd);
            TxtNewDangerCmd.Clear();
            RefreshCustomLists();
            _settingsService.Save(_settings);
        }
    }

    private void TxtNewDangerCmd_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnAddDangerCmd_Click(sender, e);
    }

    private void BtnRemoveDangerCmd_Click(object sender, RoutedEventArgs e)
    {
        if (ListDangerousCmds.SelectedItem is string selected)
        {
            _settings.DangerousCommands.Remove(selected);
            RefreshCustomLists();
            _settingsService.Save(_settings);
        }
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsFromUI();
        _settingsService.Save(_settings);
        _logService.LogSuccess("Đã lưu cài đặt thành công.");
        MessageBox.Show("Cài đặt đã được lưu vào settings.json", "Antigravity Auto Pilot", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    #endregion

    #region Window Lifecycle & Tray

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            _trayService.ShowNotification(
                "Antigravity Auto Pilot",
                "Ứng dụng đang chạy ngầm trong khay hệ thống. Bấm đúp vào biểu tượng để mở lại.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
        else
        {
            Cleanup();
        }
    }

    public void ShutdownApplication()
    {
        _isExplicitExit = true;
        Cleanup();
        System.Windows.Application.Current.Shutdown();
    }

    private void Cleanup()
    {
        UpdateSettingsFromUI();
        _settingsService.Save(_settings);

        _hotkeyService.Dispose();
        _scanner.Dispose();
        _trayService.Dispose();
        _audioService.Stop();
    }

    #endregion
}
