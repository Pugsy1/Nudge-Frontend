using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nudge.App.Services;

/// <summary>
/// Win32 window-activation helpers for the launch flow. Visual Pinball steals foreground focus the
/// instant its process is created, showing its own blank loading screen for however long the table
/// takes to load; this keeps Nudge in front instead for a fixed grace period, then hands focus to
/// Visual Pinball deliberately once that period elapses - at the maintainer's explicit request,
/// understanding this is a fixed cosmetic delay, not a real "table finished loading" signal, since
/// Visual Pinball exposes no such signal for Nudge to wait on.
/// </summary>
public interface IWindowActivationService
{
    /// <summary>Re-asserts Nudge's main window as the foreground window periodically until <paramref name="cancellationToken"/> fires.</summary>
    Task KeepForegroundAsync(CancellationToken cancellationToken);

    /// <summary>Process IDs currently matching <paramref name="processNamePrefixes"/>, captured before launching so a later lookup can tell "the table's process" apart from any other copy of Visual Pinball already open.</summary>
    IReadOnlySet<int> SnapshotProcessIds(IReadOnlyList<string> processNamePrefixes);

    /// <summary>Brings the newest window belonging to a process matching <paramref name="processNamePrefixes"/> to the foreground, ignoring anything already running at <paramref name="excludeProcessIds"/> capture time.</summary>
    void ActivateNewestProcessWindow(IReadOnlySet<int> excludeProcessIds, IReadOnlyList<string> processNamePrefixes);
}

public sealed class WindowActivationService : IWindowActivationService
{
    private static readonly TimeSpan ReassertInterval = TimeSpan.FromMilliseconds(400);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public async Task KeepForegroundAsync(CancellationToken cancellationToken)
    {
        if (Application.Current?.MainWindow is not { } window)
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(window).Handle;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SetForegroundWindow(handle);
                await Task.Delay(ReassertInterval, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (TaskCanceledException)
        {
            // Expected once the grace period ends - not an error.
        }
    }

    public IReadOnlySet<int> SnapshotProcessIds(IReadOnlyList<string> processNamePrefixes) =>
        MatchingProcesses(processNamePrefixes).Select(p => p.Id).ToHashSet();

    public void ActivateNewestProcessWindow(IReadOnlySet<int> excludeProcessIds, IReadOnlyList<string> processNamePrefixes)
    {
        Process? newest = MatchingProcesses(processNamePrefixes)
            .Where(p => !excludeProcessIds.Contains(p.Id))
            .OrderByDescending(p => SafeStartTime(p))
            .FirstOrDefault();

        if (newest is not null && newest.MainWindowHandle != IntPtr.Zero)
        {
            SetForegroundWindow(newest.MainWindowHandle);
        }
    }

    private static IEnumerable<Process> MatchingProcesses(IReadOnlyList<string> processNamePrefixes) =>
        Process.GetProcesses()
            .Where(p => processNamePrefixes.Any(prefix => p.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Process.StartTime throws for a process that has already exited between enumeration and this call - treated as "oldest possible" rather than letting a race condition crash the launch flow.</summary>
    private static DateTime SafeStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (InvalidOperationException)
        {
            return DateTime.MinValue;
        }
    }
}
