using Microsoft.Extensions.Logging;
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
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16); // ~60Hz

    private readonly IControllerReader _controllerReader;
    private readonly IKeyboardInputSynthesizer _keyboard;
    private readonly IForegroundWindowService _foregroundWindow;
    private readonly string _targetProcessName;
    private readonly ControllerMapping _mapping;
    private readonly ILogger _logger;

    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<VirtualKey> _heldKeys = [];
    private ControllerState _previousState = ControllerState.Empty;
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

        ControllerState current = hasFocus && _controllerReader.TryGetState(0, out ControllerState state)
            ? state
            : ControllerState.Empty;

        ControllerTranslationResult diff = ControllerTranslator.Translate(_previousState, current, _mapping);

        foreach (VirtualKey key in diff.KeysToPress)
        {
            _keyboard.KeyDown(key);
            _heldKeys.Add(key);
        }

        foreach (VirtualKey key in diff.KeysToRelease)
        {
            _keyboard.KeyUp(key);
            _heldKeys.Remove(key);
        }

        _previousState = current;
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
