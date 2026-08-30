namespace Nudge.Vpx.Windowing;

/// <summary>Brings a window to the foreground. Behind an interface so <see cref="TableWindowWatcher"/> is testable without a real window handle.</summary>
public interface IWindowActivator
{
    /// <summary>
    /// True if Windows accepted the request. Windows can refuse to hand over the foreground (its
    /// own anti-focus-stealing rules) - that is not an error, just something that occasionally
    /// doesn't work, the same way a browser popup sometimes opens in the background instead of on
    /// top.
    /// </summary>
    bool Activate(IntPtr windowHandle);
}
