using System;
using System.Runtime.InteropServices;

namespace AntigravityAutoPilot.Core;

public static class WindowHelper
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockSetForegroundWindow(uint uCode);

    public const uint LSFW_LOCK = 1;
    public const uint LSFW_UNLOCK = 2;

    /// <summary>
    /// Safely restores the specified window as the active foreground window,
    /// bypassing Windows foreground lock restrictions using thread attachment.
    /// </summary>
    public static void RestoreForeground(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero || !IsWindow(targetHwnd)) return;

        try
        {
            IntPtr currentForeground = GetForegroundWindow();
            if (currentForeground == targetHwnd) return; // Already foreground!

            uint currentThreadId = GetCurrentThreadId();
            uint foregroundThreadId = GetWindowThreadProcessId(currentForeground, out _);
            uint targetThreadId = GetWindowThreadProcessId(targetHwnd, out _);

            bool attachedForeground = false;
            bool attachedTarget = false;

            try
            {
                if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
                {
                    attachedForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
                }

                if (targetThreadId != 0 && targetThreadId != currentThreadId)
                {
                    attachedTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                SetForegroundWindow(targetHwnd);
                BringWindowToTop(targetHwnd);
            }
            finally
            {
                if (attachedTarget)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }
                if (attachedForeground)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }
        }
        catch
        {
            // Fallback direct set
            try { SetForegroundWindow(targetHwnd); } catch { }
        }
    }
}
