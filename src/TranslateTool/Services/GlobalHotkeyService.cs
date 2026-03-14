using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TranslateTool.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x43AA;
    private const uint WmHotkey = 0x0312;
    private const int GwlWndProc = -4;

    private IntPtr _hwnd;
    private IntPtr _oldWndProc;
    private bool _registered;
    private bool _subclassInstalled;
    private WndProcDelegate? _wndProcDelegate;

    public event EventHandler? HotkeyPressed;

    public bool Register(Window window, string hotkey)
    {
        ArgumentNullException.ThrowIfNull(window);
        Unregister();

        _hwnd = WindowNative.GetWindowHandle(window);
        if (_hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!TryParseHotkey(hotkey, out var modifiers, out var virtualKey))
        {
            modifiers = ModControl | ModShift;
            virtualKey = (uint)'A';
        }

        _wndProcDelegate = WndProc;
        var newProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        Marshal.SetLastPInvokeError(0);
        _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndProc, newProcPtr);
        var err = Marshal.GetLastPInvokeError();
        if (_oldWndProc == IntPtr.Zero && err != 0)
        {
            _wndProcDelegate = null;
            _hwnd = IntPtr.Zero;
            return false;
        }

        _subclassInstalled = true;
        _registered = RegisterHotKey(_hwnd, HotkeyId, modifiers, virtualKey);
        if (!_registered)
        {
            _ = SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);
            _subclassInstalled = false;
            _oldWndProc = IntPtr.Zero;
            _wndProcDelegate = null;
            _hwnd = IntPtr.Zero;
            return false;
        }

        return true;
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            _ = UnregisterHotKey(_hwnd, HotkeyId);
            if (_subclassInstalled && _oldWndProc != IntPtr.Zero)
            {
                _ = SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);
            }
        }

        _registered = false;
        _subclassInstalled = false;
        _oldWndProc = IntPtr.Zero;
        _hwnd = IntPtr.Zero;
        _wndProcDelegate = null;
    }

    public void Dispose()
    {
        Unregister();
        GC.SuppressFinalize(this);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (_oldWndProc == IntPtr.Zero)
        {
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    private static bool TryParseHotkey(string hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        var segments = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in segments)
        {
            var segment = raw.ToUpperInvariant();
            switch (segment)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    break;
                case "SHIFT":
                    modifiers |= ModShift;
                    break;
                case "ALT":
                    modifiers |= ModAlt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    break;
                default:
                    if (segment.Length == 1)
                    {
                        virtualKey = segment[0];
                    }
                    else if (segment.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                             && int.TryParse(segment[1..], out var fn)
                             && fn is >= 1 and <= 24)
                    {
                        virtualKey = (uint)(0x70 + fn - 1);
                    }
                    break;
            }
        }

        return modifiers != 0 && virtualKey != 0;
    }

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
