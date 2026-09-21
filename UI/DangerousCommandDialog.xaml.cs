using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AntigravityAutoPilot.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace AntigravityAutoPilot.UI;

public partial class DangerousCommandDialog : Window
{
    private readonly DangerousCommandPromptRequest _request;
    private DispatcherTimer? _copyFeedbackTimer;

    public bool UserApproved { get; private set; } = false;

    public DangerousCommandDialog(DangerousCommandPromptRequest request)
    {
        InitializeComponent();
        _request = request;

        TxtDangerousKeyword.Text = string.IsNullOrWhiteSpace(request.DangerousKeyword) ? "Phá hủy / Nguy hiểm" : request.DangerousKeyword;
        TxtActionName.Text = string.IsNullOrWhiteSpace(request.Action.ButtonText) ? (request.IsQuestionForm ? "Submit Form" : "Run") : request.Action.ButtonText;
        TxtFullCommand.Text = string.IsNullOrWhiteSpace(request.FullCommand) ? request.DangerousKeyword : request.FullCommand;
        TxtFullContext.Text = string.IsNullOrWhiteSpace(request.SurroundingContext) ? request.FullCommand : request.SurroundingContext;

        if (request.IsQuestionForm)
        {
            TxtWarningSubtitle.Text = "Auto Pilot phát hiện lệnh nguy hiểm trong biểu mẫu câu hỏi tương tác (ask_question). Vui lòng kiểm tra kỹ nội dung lệnh trước khi Duyệt hoặc Chặn.";
        }

        PreviewKeyDown += DangerousCommandDialog_PreviewKeyDown;
    }

    private void DangerousCommandDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Escape = Block and pause
            e.Handled = true;
            BtnBlock_Click(sender, new RoutedEventArgs());
        }
    }

    private void BtnCopyCommand_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string cmd = TxtFullCommand.Text;
            if (!string.IsNullOrEmpty(cmd))
            {
                Clipboard.SetText(cmd);

                TxtCopyLabel.Text = "✓ Đã sao chép!";
                TxtCopyIcon.Text = "✓";

                _copyFeedbackTimer?.Stop();
                _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _copyFeedbackTimer.Tick += (s, ev) =>
                {
                    TxtCopyLabel.Text = "Sao chép lệnh";
                    TxtCopyIcon.Text = "📋";
                    _copyFeedbackTimer.Stop();
                };
                _copyFeedbackTimer.Start();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể sao chép vào clipboard: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnBlock_Click(object sender, RoutedEventArgs e)
    {
        UserApproved = false;
        DialogResult = false;
        Close();
    }

    private void BtnApprove_Click(object sender, RoutedEventArgs e)
    {
        UserApproved = true;
        DialogResult = true;
        Close();
    }
}
