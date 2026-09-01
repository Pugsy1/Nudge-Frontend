using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;

namespace Nudge.Vpx.Windowing;

/// <inheritdoc cref="ITableWindowWatcher" />
public sealed class TableWindowWatcher : ITableWindowWatcher
{
    private const int MinimumWidth = 200;
    private const int MinimumHeight = 200;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(150);

    // A window must be found ready across polls spanning at least this long before it's trusted -
    // Visual Pinball (or any app) can briefly create a small or temporary window while it starts up
    // before its real one appears; requiring it to stay put for a moment filters that out.
    private static readonly TimeSpan DefaultStabilityWindow = TimeSpan.FromMilliseconds(450);

    // Generous: a heavy table's script-driven startup can take a while on slower hardware, and
    // giving up too early just means falling back to "wait for the process to exit" behaviour,
    // which is always safe - never a stuck state.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly IWindowSnapshotProvider _snapshotProvider;
    private readonly IWindowActivator _activator;
    private readonly IProcessLivenessChecker _livenessChecker;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _stabilityWindow;
    private readonly TimeSpan _timeout;
    private readonly ILogger<TableWindowWatcher> _logger;

    public TableWindowWatcher(
        IWindowSnapshotProvider snapshotProvider,
        IWindowActivator activator,
        IProcessLivenessChecker livenessChecker,
        ILogger<TableWindowWatcher> logger)
        : this(snapshotProvider, activator, livenessChecker, DefaultPollInterval, DefaultStabilityWindow, DefaultTimeout, logger)
    {
    }

    internal TableWindowWatcher(
        IWindowSnapshotProvider snapshotProvider,
        IWindowActivator activator,
        IProcessLivenessChecker livenessChecker,
        TimeSpan pollInterval,
        TimeSpan stabilityWindow,
        TimeSpan timeout,
        ILogger<TableWindowWatcher> logger)
    {
        _snapshotProvider = snapshotProvider;
        _activator = activator;
        _livenessChecker = livenessChecker;
        _pollInterval = pollInterval;
        _stabilityWindow = stabilityWindow;
        _timeout = timeout;
        _logger = logger;
    }

    public async Task<bool> ActivateWhenReadyAsync(int processId, CancellationToken cancellationToken = default)
    {
        var elapsed = Stopwatch.StartNew();
        IntPtr candidateWindow = IntPtr.Zero;
        Stopwatch? candidateStableSince = null;

        while (elapsed.Elapsed < _timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_livenessChecker.IsRunning(processId))
            {
                return false;
            }

            IntPtr? window = _snapshotProvider.FindReadyWindow(processId, MinimumWidth, MinimumHeight);

            if (window is { } handle)
            {
                if (handle == candidateWindow && candidateStableSince is not null)
                {
                    if (candidateStableSince.Elapsed >= _stabilityWindow)
                    {
                        // Already in front on its own, which is the usual case: leave its windows
                        // exactly as Visual Pinball arranged them.
                        //
                        // This used to activate unconditionally, and that reordered VPX's own
                        // windows. VPX opens a playfield and, when it is enabled, a separate score
                        // display; the search below only recognises a window at least 200x200, which
                        // a DMD strip is not, so the playfield was always the one picked - and
                        // forcing it to the foreground put it over the DMD and hid it, on every
                        // table. Nudge's activation exists only to stop a table opening behind
                        // Nudge's own window, so it is only needed when Nudge (or anything else) is
                        // still in front.
                        if (_snapshotProvider.IsForeground(processId))
                        {
                            _logger.LogDebug(
                                "The table window is already in the foreground; leaving its window order alone.");
                            return true;
                        }

                        // Best-effort: Windows' own anti-focus-stealing rules can legitimately
                        // decline this independently of whether the window is genuinely ready (see
                        // the interface's remarks) - a declined foreground steal is not treated as
                        // "not ready".
                        if (!_activator.Activate(handle))
                        {
                            _logger.LogDebug(
                                "The table window was found ready, but Windows declined to bring it to the foreground.");
                        }

                        return true;
                    }
                }
                else
                {
                    candidateWindow = handle;
                    candidateStableSince = Stopwatch.StartNew();
                }
            }
            else
            {
                candidateWindow = IntPtr.Zero;
                candidateStableSince = null;
            }

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("Timed out after {Timeout} waiting for the table window to become ready.", _timeout);
        return false;
    }
}
