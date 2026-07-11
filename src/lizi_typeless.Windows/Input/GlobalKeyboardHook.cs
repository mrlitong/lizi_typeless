using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using lizi_typeless.Windows.Infrastructure;

namespace lizi_typeless.Windows.Input;

internal sealed class GlobalKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int VkRmenu = 0xA5;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly LowLevelKeyboardProc _callback;
    private nint _hook;

    public GlobalKeyboardHook()
    {
        _callback = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = GetModuleHandle(module?.ModuleName);
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, moduleHandle, 0);
        if (_hook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install keyboard hook.");
        }
    }

    public event EventHandler<RightAltEventArgs>? RightAltChanged;

    public void Dispose()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        if (!UnhookWindowsHookEx(_hook))
        {
            DiagnosticLog.Write($"UnhookWindowsHookEx failed with {Marshal.GetLastWin32Error()}.");
        }

        _hook = nint.Zero;
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == VkRmenu)
        {
            var messageId = unchecked((int)message);
            var isDown = messageId is WmKeyDown or WmSysKeyDown;
            var isUp = messageId is WmKeyUp or WmSysKeyUp;
            if (isDown || isUp)
            {
                try
                {
                    RightAltChanged?.Invoke(this, new RightAltEventArgs(isDown, DateTimeOffset.Now));
                }
                catch (Exception exception)
                {
                    DiagnosticLog.Write("Right Alt event handler failed.", exception);
                }
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
}

internal sealed record RightAltEventArgs(bool IsKeyDown, DateTimeOffset Timestamp);
