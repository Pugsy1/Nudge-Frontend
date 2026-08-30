using System.IO.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Nudge.Library.Tests;

/// <summary>
/// <see cref="TableFolderWatcher"/> exists so the library notices a table being added or removed
/// without the user needing to click "Rescan" - see docs/RESEARCH-NOTES.md. Tested against a real
/// temporary folder and the real <see cref="FileSystem"/> rather than <c>MockFileSystem</c>: this
/// version of System.IO.Abstractions.TestingHelpers has no built-in <c>FileSystemWatcher</c>
/// simulation (confirmed by running against it first - it throws
/// <see cref="NotImplementedException"/>), and a hand-rolled fake watcher would only prove the fake
/// behaves as coded, not that this class works with a real one. A short debounce interval keeps
/// these fast.
/// </summary>
public sealed class TableFolderWatcherTests : IDisposable
{
    private static readonly TimeSpan ShortDebounce = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly string _tablesPath = Directory.CreateTempSubdirectory("NudgeTableFolderWatcherTests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tablesPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a lingering watcher handle briefly holding the folder open on a
            // slow CI machine is not worth failing the test suite over.
        }
    }

    [Fact]
    public async Task Fires_after_a_file_is_added()
    {
        var watcher = new TableFolderWatcher(_fileSystem, ShortDebounce, NullLogger<TableFolderWatcher>.Instance);
        var signal = new SignalCounter();

        using IDisposable session = watcher.Watch(_tablesPath, signal.SignalAsync);
        await File.WriteAllBytesAsync(Path.Combine(_tablesPath, "NewTable.vpx"), [1, 2, 3]);

        await signal.WaitAsync(WaitTimeout);
        signal.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Fires_after_a_file_is_removed()
    {
        string path = Path.Combine(_tablesPath, "ToDelete.vpx");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var watcher = new TableFolderWatcher(_fileSystem, ShortDebounce, NullLogger<TableFolderWatcher>.Instance);
        var signal = new SignalCounter();

        using IDisposable session = watcher.Watch(_tablesPath, signal.SignalAsync);
        File.Delete(path);

        await signal.WaitAsync(WaitTimeout);
        signal.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task A_burst_of_changes_debounces_to_a_single_callback()
    {
        var watcher = new TableFolderWatcher(_fileSystem, TimeSpan.FromMilliseconds(300), NullLogger<TableFolderWatcher>.Instance);
        var signal = new SignalCounter();

        using IDisposable session = watcher.Watch(_tablesPath, signal.SignalAsync);
        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(_tablesPath, $"Table{i}.vpx"), [1]);
        }

        await signal.WaitAsync(WaitTimeout);
        await Task.Delay(TimeSpan.FromMilliseconds(400)); // let any extra, wrongly-un-debounced calls land
        signal.Count.Should().Be(1, "five near-simultaneous events are one logical change, not five");
    }

    [Fact]
    public async Task Disposing_the_session_stops_further_callbacks()
    {
        var watcher = new TableFolderWatcher(_fileSystem, ShortDebounce, NullLogger<TableFolderWatcher>.Instance);
        var signal = new SignalCounter();

        IDisposable session = watcher.Watch(_tablesPath, signal.SignalAsync);
        session.Dispose();
        await File.WriteAllBytesAsync(Path.Combine(_tablesPath, "AfterDispose.vpx"), [1]);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        signal.Count.Should().Be(0);
    }

    [Fact]
    public void Watching_a_folder_that_does_not_exist_returns_a_harmless_no_op_handle()
    {
        var watcher = new TableFolderWatcher(_fileSystem, ShortDebounce, NullLogger<TableFolderWatcher>.Instance);

        IDisposable session = watcher.Watch(Path.Combine(_tablesPath, "DoesNotExist"), () => Task.CompletedTask);

        session.Dispose(); // must not throw
    }

    private sealed class SignalCounter
    {
        private readonly SemaphoreSlim _signal = new(0);

        public int Count { get; private set; }

        public Task SignalAsync()
        {
            Count++;
            _signal.Release();
            return Task.CompletedTask;
        }

        public async Task WaitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await _signal.WaitAsync(cts.Token);
        }
    }
}
