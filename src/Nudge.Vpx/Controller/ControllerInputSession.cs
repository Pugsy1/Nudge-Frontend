using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;

namespace Nudge.Vpx.Controller;

/// <summary>
/// One active controller-to-keyboard translation, from <see cref="ControllerInputService.StartTranslating"/>
/// until disposed. Polls at 60Hz on a background loop; <see cref="Tick"/> is the actual per-poll
/// logic, exposed internally so tests can drive it directly and deterministically rather than racing
/// a real timer.
/// </summary>
internal sealed class ControllerInputSession : IDisposable
{
    /// <summary>
    /// ~125Hz. This is flipper latency: a button is not seen until the next poll, so the interval is
    /// added to every press in the one place a player can actually feel it. Halved from 16ms, which
    /// is a frame at 60Hz and enough to be perceptible on a fast shot.
    ///
    /// Not taken further: XInput is polled, and past roughly this rate the reads start costing more
    /// than the latency they save, with the controller's own USB reporting interval setting a floor
    /// this cannot get under anyway.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(8);

    /// <summary>How many polls to skip after finding no controller connected - see <see cref="ReadController"/>. ~1 second at <see cref="PollInterval"/>.</summary>
    private const int DisconnectedProbeIntervalTicks = 125;

    private readonly IControllerReader _controllerReader;
    private readonly IKeyboardInputSynthesizer _keyboard;
    private readonly IForegroundWindowService _foregroundWindow;
    private readonly string _targetProcessName;
    private readonly ControllerMapping _mapping;
    private readonly ILogger _logger;

    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<VirtualKey> _heldKeys = [];
    private ControllerState _previousState = ControllerState.Empty;
    private bool _hadFocus;
    private int _ticksUntilReconnectProbe;
    private Task? _loopTask;
    private int _disposed;

    public ControllerInputSession(
        IControllerReader controllerReader,
        IKeyboardInputSynthesizer keyboard,
        IForegroundWindowService foregroundWindow,
        string targetProcessName,
        ControllerMapping mapping,
        ILogger logger)
    {
        _controllerReader = controllerReader;
        _keyboard = keyboard;
        _foregroundWindow = foregroundWindow;
        _targetProcessName = targetProcessName;
        _mapping = mapping;
        _logger = logger;
    }

    public void Start() => _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via Dispose.
        }
    }

    /// <summary>
    /// One poll: reads the controller (only if <see cref="_targetProcessName"/> currently owns the
    /// foreground window - otherwise treated as "nothing pressed", which also naturally forces the
    /// release of anything still held from before focus was lost), diffs against the previous
    /// state, and sends whatever key transitions resulted.
    /// </summary>
    internal void Tick()
    {
        string? foregroundProcessName = _foregroundWindow.GetForegroundProcessName();
        bool hasFocus = string.Equals(foregroundProcessName, _targetProcessName, StringComparison.OrdinalIgnoreCase);

        ControllerState current = hasFocus ? ReadController() : ControllerState.Empty;

        // Focus has just arrived: adopt whatever is physically held right now as the baseline rather
        // than treating it as a fresh press. This is the seam between browsing the library with a pad
        // and playing with it, and without this that seam is visibly broken - the A press that
        // launched the table from Nudge's own library is still held for the fraction of a second it
        // takes Visual Pinball to take focus, so translation would start by reading it as a brand-new
        // press and fire a phantom plunger before the player has touched anything. A direction still
        // held from navigating does the same thing as a phantom nudge.
        if (hasFocus && !_hadFocus)
        {
            _hadFocus = true;
            _previousState = current;
            return;
        }

        _hadFocus = hasFocus;

        ControllerTranslationResult diff = ControllerTranslator.Translate(_previousState, current, _mapping);

        foreach (VirtualKey key in diff.KeysToPress)
        {
            _keyboard.KeyDown(key);
            _heldKeys.Add(key);
        }

        foreach (VirtualKey key in diff.KeysToRelease)
        {
            // Only release what this session actually pressed. A button adopted as the focus baseline
            // above was never pressed down here, so releasing it would send the running table a
            // key-up it never saw a matching key-down for.
            if (_heldKeys.Remove(key))
            {
                _keyboard.KeyUp(key);
            }
        }

        _previousState = current;
    }

    /// <summary>
    /// The controller's current state, or nothing-pressed when no pad is connected.
    ///
    /// Backs off rather than asking every single tick while nothing is connected. Querying XInput
    /// for an empty slot is not free - it goes out and enumerates devices, which Microsoft's own
    /// guidance warns against doing every frame - and this loop runs for the entire play session, so
    /// on a machine with no controller at all that cost would be paid 60 times a second the whole
    /// time a table is running, stealing time from the physics simulation for nothing. Probing once
    /// a second instead is invisible to someone plugging a pad in mid-session, while a connected
    /// controller is still read at the full rate with no added latency.
    /// </summary>
    private ControllerState ReadController()
    {
        if (_ticksUntilReconnectProbe > 0)
        {
            _ticksUntilReconnectProbe--;
            return ControllerState.Empty;
        }

        if (_controllerReader.TryGetState(0, out ControllerState state))
        {
            return state;
        }

        _ticksUntilReconnectProbe = DisconnectedProbeIntervalTicks;
        return ControllerState.Empty;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            // Expected from the loop's own cancellation surfacing through Wait().
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Controller input loop did not shut down cleanly.");
        }

        // Defensive: never leave a key stuck down because the session stopped mid-hold (the target
        // process exited, the maintainer disabled the setting, etc).
        foreach (VirtualKey key in _heldKeys)
        {
            _keyboard.KeyUp(key);
        }

        _heldKeys.Clear();
        _cts.Dispose();
    }
}
