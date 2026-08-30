using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nudge.Vpx.Controller;

/// <inheritdoc cref="IForegroundWindowService" />
public sealed class WindowsForegroundWindowService : IForegroundWindowService
{
    public string? GetForegroundProcessName()
    {
        IntPtr windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(windowHandle, out uint processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // The process exited between reading its id and looking it up - treat as "unknown",
            // same as no foreground window at all.
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
