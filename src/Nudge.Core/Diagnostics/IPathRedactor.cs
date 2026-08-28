namespace Nudge.Core.Diagnostics;

/// <summary>
/// Removes the Windows username from text that is about to be written to a log.
///
/// Nudge logs full paths on purpose, because diagnosing a broken installation without them is
/// guesswork. But users paste logs into public forums, so the username never survives to disk.
/// </summary>
public interface IPathRedactor
{
    /// <summary>Returns <paramref name="text"/> with the current username replaced by a placeholder.</summary>
    string Redact(string? text);
}
