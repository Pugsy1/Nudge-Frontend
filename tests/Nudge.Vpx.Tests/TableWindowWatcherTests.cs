using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Vpx.Windowing;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// <see cref="TableWindowWatcher"/>'s job is entirely orchestration - debounce a window across
/// polls, give up on a timeout, bail out early if the process exits - so it's tested against fakes
/// for the three Win32 seams (<see cref="IWindowSnapshotProvider"/>, <see cref="IWindowActivator"/>,
/// <see cref="IProcessLivenessChecker"/>) with a very short poll interval, rather than against real
/// windows. The real Win32 implementations were separately smoke-tested against the actual OS APIs
/// (see docs/RESEARCH-NOTES.md) since there's no meaningful way to unit test EnumWindows itself.
/// </summary>
public sealed class TableWindowWatcherTests
{
    private const int ProcessId = 1234;
    private static readonly IntPtr WindowHandle = new(777);

    private readonly FakeSnapshotProvider _snapshots = new();
    private readonly FakeActivator _activator = new();
    private readonly FakeLivenessChecker _liveness = new() { IsRunningValue = true };

    [Fact]
    public async Task Activates_the_window_once_it_has_been_stable_for_long_enough()
    {
        _snapshots.ReadyWindow = WindowHandle;
        TableWindowWatcher watcher = CreateWatcher();

        bool result = await watcher.ActivateWhenReadyAsync(ProcessId);

        result.Should().BeTrue();
        _activator.LastActivated.Should().Be(WindowHandle);
    }

    /// <summary>
    /// Visual Pinball opens a playfield and, when it is switched on, a separate score display, and it
    /// decides how they sit relative to each other. Once VPX is in front, Nudge must not touch that:
    /// activating the playfield raised it over the DMD and hid it on every table. Nudge only needs to
    /// bring the table forward when something else - its own window - is still in front.
    /// </summary>
    [Fact]
    public async Task Leaves_the_window_order_alone_when_the_table_is_already_in_front()
    {
        _snapshots.ReadyWindow = WindowHandle;
        _snapshots.ProcessIsForeground = true;
        TableWindowWatcher watcher = CreateWatcher();

        bool result = await watcher.ActivateWhenReadyAsync(ProcessId);

        result.Should().BeTrue();
        _activator.LastActivated.Should().BeNull("VPX was already in front, so its windows must not be reordered");
    }

    [Fact]
    public async Task Still_reports_ready_even_when_Windows_declines_the_foreground_request()
    {
        // Windows' own anti-focus-stealing rules can legitimately refuse SetForegroundWindow
        // independently of whether the window itself is genuinely ready - a declined activation
        // must not be treated as "not ready", since the window most often already has focus on its
        // own anyway (it belongs to a process Nudge itself just launched).
        _snapshots.ReadyWindow = WindowHandle;
        _activator.NextActivateResult = false;
        TableWindowWatcher watcher = CreateWatcher();

        bool result = await watcher.ActivateWhenReadyAsync(ProcessId);

        result.Should().BeTrue("detection, not the foreground steal, is what 'ready' means");
        _activator.LastActivated.Should().Be(WindowHandle, "the best-effort attempt must still have been made");
    }

    [Fact]
    public async Task Returns_false_immediately_once_the_process_has_already_exited()
    {
        _liveness.IsRunningValue = false;
        TableWindowWatcher watcher = CreateWatcher();

        bool result = await watcher.ActivateWhenReadyAsync(ProcessId);

        result.Should().BeFalse();
        _activator.LastActivated.Should().BeNull();
    }

    [Fact]
    public async Task Never_activates_anything_if_no_window_ever_becomes_ready_before_the_timeout()
    {
        _snapshots.ReadyWindow = null; // never finds one
        TableWindowWatcher watcher = CreateWatcher(timeout: TimeSpan.FromMilliseconds(120));

        bool result = await watcher.ActivateWhenReadyAsync(ProcessId);

        result.Should().BeFalse();
        _activator.LastActivated.Should().BeNull();
    }

    [Fact]
    public async Task A_window_that_disappears_before_becoming_stable_resets_the_debounce()
    {
        // Flips ready/not-ready every poll, so the stability window is never reached until the
        // timeout - proving a flickering window is never mistaken for a genuinely ready one.
        _snapshots.FlipEveryPoll = true;
        _snapshots.ReadyWindow = WindowHandle;
        TableWindowWatcher watcher = CreateWatcher(timeout: TimeSpan.FromMilliseconds(150));

        bool result = await watcher.ActivateWhenReadyAsync(ProcessId);

        result.Should().BeFalse("a window that keeps disappearing before the stability window elapses must never be activated");
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_wait_rather_than_returning_a_result()
    {
        _snapshots.ReadyWindow = null;
        TableWindowWatcher watcher = CreateWatcher(timeout: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        Func<Task> act = () => watcher.ActivateWhenReadyAsync(ProcessId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private TableWindowWatcher CreateWatcher(TimeSpan? timeout = null) => new(
        _snapshots,
        _activator,
        _liveness,
        pollInterval: TimeSpan.FromMilliseconds(20),
        stabilityWindow: TimeSpan.FromMilliseconds(50),
        timeout: timeout ?? TimeSpan.FromMilliseconds(500),
        NullLogger<TableWindowWatcher>.Instance);

    private sealed class FakeSnapshotProvider : IWindowSnapshotProvider
    {
        public IntPtr? ReadyWindow { get; set; }

        public bool FlipEveryPoll { get; set; }

        /// <summary>Whether the watched process already owns the foreground window.</summary>
        public bool ProcessIsForeground { get; set; }

        public bool IsForeground(int processId) => ProcessIsForeground;

        private bool _toggle;

        public IntPtr? FindReadyWindow(int processId, int minimumWidth, int minimumHeight)
        {
            if (!FlipEveryPoll)
            {
                return ReadyWindow;
            }

            _toggle = !_toggle;
            return _toggle ? ReadyWindow : null;
        }
    }

    private sealed class FakeActivator : IWindowActivator
    {
        public bool NextActivateResult { get; set; } = true;

        public IntPtr? LastActivated { get; private set; }

        public bool Activate(IntPtr windowHandle)
        {
            LastActivated = windowHandle;
            return NextActivateResult;
        }
    }

    private sealed class FakeLivenessChecker : IProcessLivenessChecker
    {
        public bool IsRunningValue { get; set; }

        public bool IsRunning(int processId) => IsRunningValue;
    }
}
