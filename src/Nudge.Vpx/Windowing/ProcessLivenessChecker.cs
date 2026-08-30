using System.Diagnostics;

namespace Nudge.Vpx.Windowing;

/// <inheritdoc cref="IProcessLivenessChecker" />
public sealed class ProcessLivenessChecker : IProcessLivenessChecker
{
    public bool IsRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
