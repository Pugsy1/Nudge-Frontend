using System.Runtime.InteropServices;

namespace Nudge.Vpx.Windowing;

/// <inheritdoc cref="IWindowActivator" />
public sealed class Win32WindowActivator : IWindowActivator
{
    public bool Activate(IntPtr windowHandle) => SetForegroundWindow(windowHandle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
