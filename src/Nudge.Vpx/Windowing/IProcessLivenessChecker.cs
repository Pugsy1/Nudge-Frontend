namespace Nudge.Vpx.Windowing;

/// <summary>Checks whether a process id still refers to a running process. Behind an interface so <see cref="TableWindowWatcher"/> is testable without a real process.</summary>
public interface IProcessLivenessChecker
{
    bool IsRunning(int processId);
}
