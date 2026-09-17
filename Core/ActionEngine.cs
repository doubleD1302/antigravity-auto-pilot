using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using AntigravityAutoPilot.Models;

namespace AntigravityAutoPilot.Core;

public class ActionEngine
{
    private readonly ConcurrentDictionary<string, DateTime> _recentClicks = new();
    private readonly ConcurrentDictionary<string, bool> _consumedActionIds = new();
    private DateTime _lastPurgeTime = DateTime.UtcNow;

    public class ActionResult
    {
        public bool Success { get; set; }
        public bool WasDebounced { get; set; }
        public string MethodUsed { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Executes the click action on the specified element using InvokePattern (non-intrusive),
    /// respecting debounce timers.
    /// </summary>
    public async Task<ActionResult> ExecuteAsync(AutomationAction action, AppSettings settings, CancellationToken ct)
    {
        var result = new ActionResult();

        // 0. One-time consumption check (Proceed / approved plans)
        if (_consumedActionIds.ContainsKey(action.ElementIdentifier))
        {
            result.Success = false;
            result.WasDebounced = true;
            result.ErrorMessage = "Action already consumed permanently";
            return result;
        }

        // 1. Debounce check
        DateTime now = DateTime.UtcNow;
        PurgeOldDebounceEntries(now);

        if (_recentClicks.TryGetValue(action.ElementIdentifier, out DateTime lastClicked))
        {
            if ((now - lastClicked).TotalMilliseconds < settings.DebounceMs)
            {
                result.Success = false;
                result.WasDebounced = true;
                return result;
            }
        }

        // 2. Perform non-intrusive click with Focus Preservation
        bool clickSuccess = false;
        string method = "None";
        IntPtr priorForeground = IntPtr.Zero;

        if (settings.PreventFocusStealing)
        {
            priorForeground = WindowHelper.GetForegroundWindow();
            WindowHelper.LockSetForegroundWindow(WindowHelper.LSFW_LOCK);
        }

        try
        {
            // Primary: InvokePattern
            if (action.Element.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokeObj) &&
                invokeObj is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                clickSuccess = true;
                method = "InvokePattern";
            }
            // Secondary: TogglePattern
            else if (action.Element.TryGetCurrentPattern(TogglePattern.Pattern, out object? toggleObj) &&
                     toggleObj is TogglePattern togglePattern)
            {
                togglePattern.Toggle();
                clickSuccess = true;
                method = "TogglePattern";
            }
            // Tertiary: SelectionItemPattern
            else if (action.Element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selectObj) &&
                     selectObj is SelectionItemPattern selectPattern)
            {
                selectPattern.Select();
                clickSuccess = true;
                method = "SelectionItemPattern";
            }
            // Optional Fallback: Physical mouse click if explicitly allowed
            else if (settings.AllowPhysicalClickFallback)
            {
                clickSuccess = TryPhysicalClickFallback(action.Element);
                method = clickSuccess ? "PhysicalClickFallback" : "PhysicalClickFailed";
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "Element does not support InvokePattern and physical fallback is disabled.";
                return result;
            }
        }
        catch (ElementNotAvailableException)
        {
            result.Success = false;
            result.ErrorMessage = "Element became unavailable right before clicking.";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to invoke element: {ex.Message}";
            return result;
        }
        finally
        {
            if (settings.PreventFocusStealing)
            {
                WindowHelper.LockSetForegroundWindow(WindowHelper.LSFW_UNLOCK);
                if (priorForeground != IntPtr.Zero && WindowHelper.GetForegroundWindow() != priorForeground)
                {
                    WindowHelper.RestoreForeground(priorForeground);
                }
            }
        }

        if (clickSuccess)
        {
            _recentClicks[action.ElementIdentifier] = now;

            // One-time actions: Run, Proceed, Submit, Accept, AcceptAll, Confirm, Apply, Allow
            // are permanently consumed so they are never clicked repeatedly in chat history!
            if (action.Kind == ActionKind.Proceed ||
                action.Kind == ActionKind.Run ||
                action.Kind == ActionKind.Submit ||
                action.Kind == ActionKind.Accept ||
                action.Kind == ActionKind.AcceptAll ||
                action.Kind == ActionKind.Confirm ||
                action.Kind == ActionKind.Allow ||
                action.Kind == ActionKind.Apply)
            {
                _consumedActionIds[action.ElementIdentifier] = true;
            }

            result.Success = true;
            result.MethodUsed = method;

            // Wait for UI to update (300ms) before allowing the next scan cycle
            try
            {
                await Task.Delay(300, ct);

                // Double check if Electron delayed-focused after UI update
                if (settings.PreventFocusStealing && priorForeground != IntPtr.Zero && WindowHelper.GetForegroundWindow() != priorForeground)
                {
                    WindowHelper.RestoreForeground(priorForeground);
                }
            }
            catch (OperationCanceledException) { }
        }

        return result;
    }

    /// <summary>
    /// Selects a specified option element (radio button or option item).
    /// </summary>
    public async Task<ActionResult> SelectOptionAsync(AutomationElement optionElement, AppSettings settings, CancellationToken ct)
    {
        var result = new ActionResult();
        bool success = false;
        string method = "None";
        IntPtr priorForeground = IntPtr.Zero;

        if (settings.PreventFocusStealing)
        {
            priorForeground = WindowHelper.GetForegroundWindow();
            WindowHelper.LockSetForegroundWindow(WindowHelper.LSFW_LOCK);
        }

        try
        {
            // 1. SelectionItemPattern (Standard for RadioButtons / ListItems)
            if (optionElement.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selectObj) &&
                selectObj is SelectionItemPattern selectPattern)
            {
                selectPattern.Select();
                success = true;
                method = "SelectionItemPattern";
            }
            // 2. InvokePattern (Common for button/clickable div in Electron)
            else if (optionElement.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokeObj) &&
                     invokeObj is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                success = true;
                method = "InvokePattern";
            }
            // 3. TogglePattern
            else if (optionElement.TryGetCurrentPattern(TogglePattern.Pattern, out object? toggleObj) &&
                     toggleObj is TogglePattern togglePattern)
            {
                togglePattern.Toggle();
                success = true;
                method = "TogglePattern";
            }
            // 4. Physical click fallback if patterns unavailable
            else
            {
                success = TryPhysicalClickFallback(optionElement);
                method = success ? "PhysicalClickFallback" : "PhysicalClickFailed";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
        finally
        {
            if (settings.PreventFocusStealing)
            {
                WindowHelper.LockSetForegroundWindow(WindowHelper.LSFW_UNLOCK);
                if (priorForeground != IntPtr.Zero && WindowHelper.GetForegroundWindow() != priorForeground)
                {
                    WindowHelper.RestoreForeground(priorForeground);
                }
            }
        }

        result.Success = success;
        result.MethodUsed = method;

        if (success)
        {
            try
            {
                await Task.Delay(200, ct);

                if (settings.PreventFocusStealing && priorForeground != IntPtr.Zero && WindowHelper.GetForegroundWindow() != priorForeground)
                {
                    WindowHelper.RestoreForeground(priorForeground);
                }
            }
            catch (OperationCanceledException) { }
        }

        return result;
    }

    private void PurgeOldDebounceEntries(DateTime now)
    {
        if ((now - _lastPurgeTime).TotalSeconds < 10) return;

        _lastPurgeTime = now;
        foreach (var kvp in _recentClicks)
        {
            if ((now - kvp.Value).TotalSeconds > 15)
            {
                _recentClicks.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void ResetDebounce()
    {
        _recentClicks.Clear();
    }

    public void MarkConsumed(string elementId)
    {
        _consumedActionIds[elementId] = true;
    }

    public bool IsConsumed(string elementId)
    {
        return _consumedActionIds.ContainsKey(elementId);
    }

    public void ResetConsumed()
    {
        _consumedActionIds.Clear();
    }

    #region Win32 Physical Click Fallback

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const byte VK_RETURN = 0x0D;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public void SendEnterKey(IntPtr targetHwnd = default)
    {
        IntPtr priorForeground = IntPtr.Zero;
        if (targetHwnd != IntPtr.Zero)
        {
            priorForeground = WindowHelper.GetForegroundWindow();
            WindowHelper.SetForegroundWindow(targetHwnd);
            Thread.Sleep(30);
        }

        keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        if (priorForeground != IntPtr.Zero && priorForeground != targetHwnd)
        {
            Thread.Sleep(30);
            WindowHelper.RestoreForeground(priorForeground);
        }
    }

    public bool TryPhysicalClick(AutomationElement element, bool clickLeftEdge = false)
    {
        return TryPhysicalClickInternal(element, clickLeftEdge);
    }

    private static bool TryPhysicalClickFallback(AutomationElement element)
    {
        return TryPhysicalClickInternal(element, false);
    }

    private static bool TryPhysicalClickInternal(AutomationElement element, bool clickLeftEdge)
    {
        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return false;

            // Safety guard: Never physically click elements in the top 50px of the screen (top bar / menu bar)
            if (rect.Top < 50) return false;

            int targetX = clickLeftEdge
                ? (int)(rect.Left + Math.Min(16, Math.Max(8, rect.Width / 6)))
                : (int)(rect.Left + rect.Width / 2);
            int targetY = (int)(rect.Top + rect.Height / 2);

            GetCursorPos(out POINT originalPos);

            // Move and click
            SetCursorPos(targetX, targetY);
            Thread.Sleep(25);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(35);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            // Restore user's mouse position immediately
            SetCursorPos(originalPos.X, originalPos.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
