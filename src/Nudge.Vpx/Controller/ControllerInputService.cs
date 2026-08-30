using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;

namespace Nudge.Vpx.Controller;

/// <inheritdoc cref="IControllerInputService" />
public sealed class ControllerInputService : IControllerInputService
{
    private readonly IControllerReader _controllerReader;
    private readonly IKeyboardInputSynthesizer _keyboard;
    private readonly IForegroundWindowService _foregroundWindow;
    private readonly ILogger<ControllerInputService> _sessionLogger;

    public ControllerInputService(
        IControllerReader controllerReader,
        IKeyboardInputSynthesizer keyboard,
        IForegroundWindowService foregroundWindow,
        ILogger<ControllerInputService> sessionLogger)
    {
        _controllerReader = controllerReader;
        _keyboard = keyboard;
        _foregroundWindow = foregroundWindow;
        _sessionLogger = sessionLogger;
    }

    public IDisposable StartTranslating(string targetProcessName, ControllerMapping mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProcessName);
        ArgumentNullException.ThrowIfNull(mapping);

        var session = new ControllerInputSession(
            _controllerReader,
            _keyboard,
            _foregroundWindow,
            targetProcessName,
            mapping,
            _sessionLogger);

        session.Start();
        return session;
    }
}
