using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;

namespace Nudge.Library;

/// <inheritdoc cref="ITableFolderWatcher" />
public sealed class TableFolderWatcher : ITableFolderWatcher
{
    /// <summary>
    /// How long to wait after the last raw filesystem event before actually calling back. Long
    /// enough that copying a large (hundreds of MB) table file - which keeps touching the
    /// destination's last-write time throughout the copy - doesn't trigger a rescan of a
    /// still-growing file; the scanner is tolerant of that anyway (a following change event fires
    /// again once the copy finishes), but there is no reason to scan mid-copy at all.
    /// </summary>
    private static readonly TimeSpan DefaultDebounceInterval = TimeSpan.FromSeconds(3);

    private readonly IFileSystem _fileSystem;
    private readonly TimeSpan _debounceInterval;
    private readonly ILogger<TableFolderWatcher> _logger;

    public TableFolderWatcher(IFileSystem fileSystem, ILogger<TableFolderWatcher> logger)
        : this(fileSystem, DefaultDebounceInterval, logger)
    {
    }

    internal TableFolderWatcher(IFileSystem fileSystem, TimeSpan debounceInterval, ILogger<TableFolderWatcher> logger)
    {
        _fileSystem = fileSystem;
        _debounceInterval = debounceInterval;
        _logger = logger;
    }

    public IDisposable Watch(string tablesPath, Func<Task> onChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablesPath);
        ArgumentNullException.ThrowIfNull(onChanged);

        if (!_fileSystem.Directory.Exists(tablesPath))
        {
            _logger.LogDebug("Not watching {Path}: the folder does not exist.", tablesPath);
            return NullWatch.Instance;
        }

        return new WatchSession(_fileSystem, tablesPath, _debounceInterval, onChanged, _logger);
    }

    /// <summary>One active watch. Owns the underlying <see cref="IFileSystemWatcher"/> and the debounce timer together, so disposing one always disposes both.</summary>
    private sealed class WatchSession : IDisposable
    {
        private readonly IFileSystemWatcher _watcher;
        private readonly Timer _debounceTimer;
        private readonly TimeSpan _debounceInterval;
        private readonly Func<Task> _onChanged;
        private readonly ILogger _logger;
        private int _disposed;

        public WatchSession(IFileSystem fileSystem, string tablesPath, TimeSpan debounceInterval, Func<Task> onChanged, ILogger logger)
        {
            _debounceInterval = debounceInterval;
            _onChanged = onChanged;
            _logger = logger;
            _debounceTimer = new Timer(_ => FireAndForget());

            _watcher = fileSystem.FileSystemWatcher.New(tablesPath, "*.vpx");
            _watcher.IncludeSubdirectories = true;
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite;
            _watcher.Created += (_, _) => Debounce();
            _watcher.Deleted += (_, _) => Debounce();
            _watcher.Renamed += (_, _) => Debounce();
            _watcher.Changed += (_, _) => Debounce();
            _watcher.Error += (_, e) => _logger.LogWarning(e.GetException(), "Table folder watcher error.");
            _watcher.EnableRaisingEvents = true;
        }

        private void Debounce() => _debounceTimer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);

        private void FireAndForget() => _ = InvokeCallbackAsync();

        private async Task InvokeCallbackAsync()
        {
            try
            {
                await _onChanged().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Table folder change callback failed.");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _debounceTimer.Dispose();
        }
    }

    private sealed class NullWatch : IDisposable
    {
        public static readonly NullWatch Instance = new();

        public void Dispose()
        {
        }
    }
}
