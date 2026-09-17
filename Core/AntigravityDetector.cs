using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Core;

public class AntigravityWindowInfo
{
    public IntPtr Hwnd { get; set; }
    public int Pid { get; set; }
    public string Title { get; set; } = string.Empty;
    public AutomationElement? Element { get; set; }
}

public class AntigravityDetector
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private readonly Dictionary<IntPtr, AntigravityWindowInfo> _cachedWindows = new();

    public AntigravityConnectionState ConnectionState { get; private set; } = AntigravityConnectionState.Disconnected;
    public string DetectedTitle { get; private set; } = string.Empty;
    public int DetectedProcessId { get; private set; } = 0;

    public event Action<AntigravityConnectionState, string>? StateChanged;

    /// <summary>
    /// Finds or validates all active Antigravity IDE AutomationElements across multiple windows.
    /// </summary>
    public List<AutomationElement> GetAntigravityWindows()
    {
        // 1. Verify and clean up closed/invisible cached windows
        var toRemove = new List<IntPtr>();
        foreach (var kvp in _cachedWindows)
        {
            IntPtr hWnd = kvp.Key;
            if (!IsWindow(hWnd) || !IsWindowVisible(hWnd))
            {
                toRemove.Add(hWnd);
                continue;
            }

            try
            {
                if (kvp.Value.Element != null)
                {
                    _ = kvp.Value.Element.Current.NativeWindowHandle;
                }
                else
                {
                    toRemove.Add(hWnd);
                }
            }
            catch
            {
                toRemove.Add(hWnd);
            }
        }

        foreach (var hWnd in toRemove)
        {
            _cachedWindows.Remove(hWnd);
        }

        // 2. Discover all open Antigravity windows
        var discovered = DiscoverAllAntigravityWindows();
        foreach (var info in discovered)
        {
            if (!_cachedWindows.ContainsKey(info.Hwnd))
            {
                try
                {
                    var elem = AutomationElement.FromHandle(info.Hwnd);
                    if (elem != null)
                    {
                        info.Element = elem;
                        _cachedWindows[info.Hwnd] = info;
                    }
                }
                catch { }
            }
            else
            {
                // Update title if changed
                _cachedWindows[info.Hwnd].Title = info.Title;
            }
        }

        var result = _cachedWindows.Values
            .Where(w => w.Element != null)
            .Select(w => w.Element!)
            .ToList();

        if (result.Count > 0)
        {
            DetectedTitle = _cachedWindows.Values.First().Title;
            DetectedProcessId = _cachedWindows.Values.First().Pid;

            string msg = result.Count == 1
                ? $"Đã kết nối tới Antigravity ({DetectedTitle})"
                : $"Đã kết nối tới {result.Count} cửa sổ Antigravity";

            UpdateState(AntigravityConnectionState.Connected, msg);
        }
        else
        {
            UpdateState(AntigravityConnectionState.Disconnected, "Đang chờ Antigravity...");
        }

        return result;
    }

    /// <summary>
    /// Compatibility helper: returns first available Antigravity window.
    /// </summary>
    public AutomationElement? GetAntigravityWindow()
    {
        return GetAntigravityWindows().FirstOrDefault();
    }

    private List<AntigravityWindowInfo> DiscoverAllAntigravityWindows()
    {
        var results = new List<AntigravityWindowInfo>();

        int currentPid = Environment.ProcessId;

        var processes = Process.GetProcesses()
            .Where(p =>
            {
                try
                {
                    if (p.Id == currentPid) return false;
                    return p.ProcessName.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) ||
                           p.ProcessName.Contains("antigravity", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            })
            .ToList();

        var antigravityPids = new HashSet<int>(processes.Select(p => p.Id));
        var addedHwnds = new HashSet<IntPtr>();

        // Check MainWindowHandle first
        foreach (var p in processes)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && IsWindowVisible(p.MainWindowHandle) && !addedHwnds.Contains(p.MainWindowHandle))
                {
                    string title = p.MainWindowTitle;
                    if (!string.IsNullOrEmpty(title) && !title.StartsWith("Antigravity Auto Pilot", StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new AntigravityWindowInfo { Hwnd = p.MainWindowHandle, Pid = p.Id, Title = title });
                        addedHwnds.Add(p.MainWindowHandle);
                    }
                }
            }
            catch { }
        }

        // Enumerate all top-level windows to discover multiple workspaces / child windows
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd) || addedHwnds.Contains(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if ((int)pid == currentPid) return true;

            bool isAntigravityPid = antigravityPids.Contains((int)pid);

            var sbTitle = new StringBuilder(512);
            GetWindowText(hWnd, sbTitle, 512);
            string title = sbTitle.ToString().Trim();

            if (title.StartsWith("Antigravity Auto Pilot", StringComparison.OrdinalIgnoreCase)) return true;

            var sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, 256);
            string className = sbClass.ToString().Trim();

            bool matches = false;

            if (isAntigravityPid && (className.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(title)))
            {
                matches = true;
            }
            else if (title.Contains("Antigravity", StringComparison.OrdinalIgnoreCase))
            {
                matches = true;
            }

            if (matches && !string.IsNullOrEmpty(title) && !title.StartsWith("Antigravity Auto Pilot", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new AntigravityWindowInfo { Hwnd = hWnd, Pid = (int)pid, Title = title });
                addedHwnds.Add(hWnd);
            }

            return true; // Continue enumeration to find ALL Antigravity windows
        }, IntPtr.Zero);

        return results;
    }

    public void ResetCache()
    {
        _cachedWindows.Clear();
        DetectedTitle = string.Empty;
        DetectedProcessId = 0;
    }

    private void UpdateState(AntigravityConnectionState newState, string message)
    {
        if (ConnectionState != newState)
        {
            ConnectionState = newState;
            StateChanged?.Invoke(newState, message);
        }
    }
}
