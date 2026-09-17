using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AntigravityAutoPilot.Services;

public class HotkeyService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    // Modifiers
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    // Virtual Keys
    private const uint VK_F11 = 0x7A;
    private const uint VK_F12 = 0x7B;

    private const int HOTKEY_ID_PAUSE = 9001;
    private const int HOTKEY_ID_RESUME = 9002;

    private IntPtr _windowHandle = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isRegistered = false;

    public event Action? PauseRequested;
    public event Action? ResumeRequested;

    public void Register(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        if (_windowHandle == IntPtr.Zero)
        {
            // Window might not be loaded yet
            window.SourceInitialized += (s, e) =>
            {
                _windowHandle = new WindowInteropHelper(window).Handle;
                SetupHook();
            };
        }
        else
        {
            SetupHook();
        }
    }

    private void SetupHook()
    {
        if (_isRegistered || _windowHandle == IntPtr.Zero) return;

        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(HwndHook);

        uint modifiers = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;

        // Register Ctrl + Shift + F12 (Pause / Emergency Stop)
        RegisterHotKey(_windowHandle, HOTKEY_ID_PAUSE, modifiers, VK_F12);

        // Register Ctrl + Shift + F11 (Resume / Start)
        RegisterHotKey(_windowHandle, HOTKEY_ID_RESUME, modifiers, VK_F11);

        _isRegistered = true;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            if (hotkeyId == HOTKEY_ID_PAUSE)
            {
                PauseRequested?.Invoke();
                handled = true;
            }
            else if (hotkeyId == HOTKEY_ID_RESUME)
            {
                ResumeRequested?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_isRegistered && _windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID_PAUSE);
            UnregisterHotKey(_windowHandle, HOTKEY_ID_RESUME);
            _hwndSource?.RemoveHook(HwndHook);
            _isRegistered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
    }
}
