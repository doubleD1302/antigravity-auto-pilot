using System;

namespace AntigravityAutoPilot.Models;

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string TimeFormatted => Timestamp.ToString("HH:mm:ss");
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string? Tag { get; set; }

    public string BadgeColor => Level switch
    {
        LogLevel.Success => "#22c55e", // Green
        LogLevel.Warning => "#eab308", // Yellow
        LogLevel.Danger => "#ef4444",  // Red
        _ => "#38bdf8"                 // Cyan/Blue
    };

    public string LevelName => Level switch
    {
        LogLevel.Success => "THÀNH CÔNG",
        LogLevel.Warning => "CẢNH BÁO",
        LogLevel.Danger => "NGUY HIỂM",
        _ => "THÔNG TIN"
    };

    public override string ToString()
    {
        return $"[{TimeFormatted}] [{LevelName}] {Message}";
    }
}
