using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using AntigravityAutoPilot.Models;

using Application = System.Windows.Application;

namespace AntigravityAutoPilot.Services;

public class LogService
{
    private const int MaxEntries = 1000;
    private readonly object _fileLock = new();
    private readonly string _logsDirectory;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public event Action<LogEntry>? EntryAdded;

    public LogService(string? customLogsDir = null)
    {
        _logsDirectory = customLogsDir ?? Path.Combine(AppContext.BaseDirectory, "logs");
        try
        {
            if (!Directory.Exists(_logsDirectory))
            {
                Directory.CreateDirectory(_logsDirectory);
            }
        }
        catch { }
    }

    public void LogInfo(string message, string? tag = null)
    {
        Log(message, LogLevel.Info, tag);
    }

    public void LogSuccess(string message, string? tag = null)
    {
        Log(message, LogLevel.Success, tag);
    }

    public void LogWarning(string message, string? tag = null)
    {
        Log(message, LogLevel.Warning, tag);
    }

    public void LogDanger(string message, string? tag = null)
    {
        Log(message, LogLevel.Danger, tag);
    }

    public void Log(string message, LogLevel level = LogLevel.Info, string? tag = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Level = level,
            Tag = tag
        };

        // UI update
        if (Application.Current != null && Application.Current.Dispatcher != null)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                AddUiEntry(entry);
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(() => AddUiEntry(entry));
            }
        }
        else
        {
            AddUiEntry(entry);
        }

        // File logging
        WriteToFile(entry);
    }

    private void AddUiEntry(LogEntry entry)
    {
        lock (Entries)
        {
            if (Entries.Count >= MaxEntries)
            {
                Entries.RemoveAt(0);
            }
            Entries.Add(entry);
        }
        EntryAdded?.Invoke(entry);
    }

    public void Clear()
    {
        if (Application.Current != null && Application.Current.Dispatcher != null)
        {
            Application.Current.Dispatcher.Invoke(() => Entries.Clear());
        }
        else
        {
            Entries.Clear();
        }
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            string filePath = Path.Combine(_logsDirectory, $"{dateStr}.log");

            lock (_fileLock)
            {
                File.AppendAllText(filePath, entry.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Avoid failing application on file I/O errors
        }
    }

    public string GetTodayLogFilePath()
    {
        string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(_logsDirectory, $"{dateStr}.log");
    }

    public string LogsDirectory => _logsDirectory;
}
