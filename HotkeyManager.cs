using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PushToText;

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WhKeyboardLl = 13;

    private const int CopyHotkeyId = 1;
    private const int ToggleMicHotkeyId = 2;

    private readonly Window _window;
    private readonly IntPtr _windowHandle;

    private HwndSource? _hwndSource;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private LowLevelKeyboardProc? _keyboardProc;
    private bool _micKeyHeld;
    private AppSettings _settings;

    public event EventHandler? StartRecordingRequested;
    public event EventHandler? StopRecordingRequested;
    public event EventHandler? CopyRequested;

    public HotkeyManager(Window window, AppSettings settings)
    {
        _window = window;
        _windowHandle = new WindowInteropHelper(window).Handle;
        _settings = settings;
    }

    public void Initialize()
    {
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);
        RegisterHotkeys();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _micKeyHeld = false;
        RegisterHotkeys();
    }

    public void Dispose()
    {
        UnregisterHotkeys();
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        var hotkeyId = wParam.ToInt32();
        if (hotkeyId == CopyHotkeyId)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
            return IntPtr.Zero;
        }

        if (hotkeyId == ToggleMicHotkeyId && _settings.RecordingMode == RecordingMode.Toggle)
        {
            if (_micKeyHeld)
            {
                _micKeyHeld = false;
                StopRecordingRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _micKeyHeld = true;
                StartRecordingRequested?.Invoke(this, EventArgs.Empty);
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();

        var copyRegistered = RegisterHotKey(
            _windowHandle,
            CopyHotkeyId,
            (uint)(_settings.CopyHotkey.Modifiers | HotkeyModifiers.NoRepeat),
            _settings.CopyHotkey.VirtualKey);

        if (!copyRegistered)
        {
            Debug.WriteLine("Copy hotkey registration failed.");
        }

        if (_settings.RecordingMode == RecordingMode.Toggle)
        {
            var micRegistered = RegisterHotKey(
                _windowHandle,
                ToggleMicHotkeyId,
                (uint)(_settings.MicrophoneHotkey.Modifiers | HotkeyModifiers.NoRepeat),
                _settings.MicrophoneHotkey.VirtualKey);

            if (!micRegistered)
            {
                Debug.WriteLine("Microphone hotkey registration failed.");
            }
        }
        else
        {
            InstallPushModeHook();
        }
    }

    private void UnregisterHotkeys()
    {
        UnregisterHotKey(_windowHandle, CopyHotkeyId);
        UnregisterHotKey(_windowHandle, ToggleMicHotkeyId);
        RemovePushModeHook();
    }

    private void InstallPushModeHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            return;
        }

        _keyboardProc = KeyboardProc;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = GetModuleHandle(module?.ModuleName);

        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
    }

    private void RemovePushModeHook()
    {
        if (_keyboardHook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _keyboardProc = null;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _settings.RecordingMode == RecordingMode.Push)
        {
            var keyData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var message = wParam.ToInt32();

            if (keyData.VirtualKeyCode == _settings.MicrophoneHotkey.VirtualKey && AreModifiersPressed(_settings.MicrophoneHotkey.Modifiers))
            {
                if ((message == WmKeyDown || message == WmSysKeyDown) && !_micKeyHeld)
                {
                    _micKeyHeld = true;
                    _window.Dispatcher.BeginInvoke(() => StartRecordingRequested?.Invoke(this, EventArgs.Empty));
                }
                else if ((message == WmKeyUp || message == WmSysKeyUp) && _micKeyHeld)
                {
                    _micKeyHeld = false;
                    _window.Dispatcher.BeginInvoke(() => StopRecordingRequested?.Invoke(this, EventArgs.Empty));
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool AreModifiersPressed(HotkeyModifiers modifiers)
    {
        return IsModifierStateValid(0x11, modifiers.HasFlag(HotkeyModifiers.Control))
               && IsModifierStateValid(0x10, modifiers.HasFlag(HotkeyModifiers.Shift))
               && IsModifierStateValid(0x12, modifiers.HasFlag(HotkeyModifiers.Alt))
               && IsModifierStateValid(0x5B, modifiers.HasFlag(HotkeyModifiers.Win));
    }

    private static bool IsModifierStateValid(int virtualKey, bool expectedPressed)
    {
        var state = (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        return state == expectedPressed;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
