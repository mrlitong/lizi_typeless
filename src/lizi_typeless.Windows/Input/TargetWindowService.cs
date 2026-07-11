using System.Runtime.InteropServices;
using System.Text;
using lizi_typeless.Core.Sessions;

namespace lizi_typeless.Windows.Input;

internal static class TargetWindowService
{
    public static TargetWindowInfo Capture()
    {
        var handle = GetForegroundWindow();
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException("No foreground window is available.");
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        var titleLength = GetWindowTextLength(handle);
        var title = new StringBuilder(titleLength + 1);
        _ = GetWindowText(handle, title, title.Capacity);
        return new TargetWindowInfo(handle.ToInt64(), processId, title.ToString());
    }

    public static bool IsStillForeground(TargetWindowInfo target)
    {
        if (target.ProcessId == Environment.ProcessId)
        {
            return false;
        }

        var handle = new nint(target.Handle);
        if (!IsWindow(handle) || GetForegroundWindow() != handle)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        return processId == target.ProcessId;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(nint window);
}
