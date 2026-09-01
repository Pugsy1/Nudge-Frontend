using System.Runtime.InteropServices;

namespace Nudge.Vpx.Windowing;

/// <inheritdoc cref="IWindowSnapshotProvider" />
public sealed class Win32WindowSnapshotProvider : IWindowSnapshotProvider
{
    public IntPtr? FindReadyWindow(int processId, int minimumWidth, int minimumHeight)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowProcessId);
            if (windowProcessId != (uint)processId || !IsWindowVisible(hWnd))
            {
                return true; // keep enumerating
            }

            if (!GetWindowRect(hWnd, out Rect rect))
            {
                return true;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < minimumWidth || height < minimumHeight)
            {
                return true;
            }

            found = hWnd;
            return false; // stop enumerating - found it
        }, IntPtr.Zero);

        return found == IntPtr.Zero ? null : found;
    }

    public bool IsForeground(int processId)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
        return foregroundProcessId == (uint)processId;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
