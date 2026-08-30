namespace Nudge.Core.Abstractions;

/// <summary>
/// Watches a tables folder for <c>.vpx</c> files being added, removed, or renamed on disk, so the
/// library can trigger a fresh <see cref="IVpxLibraryScanner"/> pass without the user needing to
/// remember to click "Rescan" every time they drop a table into the folder. See
/// docs/RESEARCH-NOTES.md.
/// </summary>
public interface ITableFolderWatcher
{
    /// <summary>
    /// Starts watching <paramref name="tablesPath"/> (recursively) and invokes
    /// <paramref name="onChanged"/> after a burst of filesystem activity settles down - copying one
    /// large table file raises many raw events for what is really a single logical change, so this
    /// is debounced rather than firing once per event. Disposing the returned handle stops
    /// watching; safe to call even if the path does not exist (a no-op handle is returned).
    /// </summary>
    IDisposable Watch(string tablesPath, Func<Task> onChanged);
}
