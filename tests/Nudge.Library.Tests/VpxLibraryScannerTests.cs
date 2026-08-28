using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Data;
using Nudge.Data.Repositories;
using Nudge.TestSupport;
using Xunit;

namespace Nudge.Library.Tests;

/// <summary>
/// The scanner is tested with every real production class wired together - the real
/// <see cref="Nudge.Vpx.TableFiles.VpxTableFileReader"/> reading real synthetic OLE files through a
/// mock filesystem, and a real <see cref="TableRepository"/> against real, in-memory SQLite. Only
/// the filesystem and the database location are faked; everything in between is the real code path.
/// </summary>
public sealed class VpxLibraryScannerTests : IDisposable
{
    private const string InstallationId = "install-1";
    private const string TablesPath = @"D:\VPX\Tables";

    private readonly MockFileSystem _fileSystem = new();
    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task Scans_every_vpx_file_in_the_folder()
    {
        AddTable("Medieval Madness.vpx", tableName: "Medieval Madness");
        AddTable("Twilight Zone (Bally 1993).vpx", tableName: "Twilight Zone");

        ScanResult result = await ScanAsync();

        result.TotalFilesFound.Should().Be(2);
        result.Scanned.Should().Be(2);
        result.Failed.Should().Be(0);

        IReadOnlyList<VpxTableFile> stored = await GetAllAsync();
        stored.Should().HaveCount(2);
    }

    [Fact]
    public async Task Non_vpx_files_in_the_folder_are_ignored()
    {
        AddTable("Real Table.vpx", tableName: "Real Table");
        _fileSystem.AddFile(_fileSystem.Path.Combine(TablesPath, "readme.txt"), new MockFileData("not a table"));
        _fileSystem.AddFile(_fileSystem.Path.Combine(TablesPath, "screenshot.png"), new MockFileData([1, 2, 3]));

        ScanResult result = await ScanAsync();

        result.TotalFilesFound.Should().Be(1);
    }

    [Fact]
    public async Task Files_in_subfolders_are_found_too()
    {
        AddTable(@"SubFolder\Nested Table.vpx", tableName: "Nested Table");

        ScanResult result = await ScanAsync();

        result.TotalFilesFound.Should().Be(1);
        result.Scanned.Should().Be(1);
    }

    [Fact]
    public async Task An_unreadable_file_counts_as_failed_without_stopping_the_rest_of_the_scan()
    {
        AddTable("Good Table.vpx", tableName: "Good Table");
        string badPath = _fileSystem.Path.Combine(TablesPath, "corrupt.vpx");
        _fileSystem.AddFile(badPath, new MockFileData(SyntheticVpxFile.NotAnOleFile()));

        ScanResult result = await ScanAsync();

        result.TotalFilesFound.Should().Be(2);
        result.Scanned.Should().Be(1);
        result.Failed.Should().Be(1);
        result.FailedPaths.Should().ContainSingle().Which.Should().Contain("corrupt.vpx");

        // The good table must still have made it into the database.
        IReadOnlyList<VpxTableFile> stored = await GetAllAsync();
        stored.Should().ContainSingle(t => t.DisplayTitle == "Good Table");
    }

    [Fact]
    public async Task Rescanning_an_unchanged_file_skips_it_instead_of_re_reading()
    {
        AddTable("Unchanged.vpx", tableName: "Unchanged");

        ScanResult first = await ScanAsync();
        first.Scanned.Should().Be(1);
        first.Skipped.Should().Be(0);

        ScanResult second = await ScanAsync();
        second.Scanned.Should().Be(0, "nothing changed since the first scan");
        second.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task A_file_whose_content_changed_is_rescanned_not_skipped()
    {
        string path = AddTable("Changing.vpx", tableName: "Original");
        await ScanAsync();

        // Simulate the table being re-saved with new content: different bytes, later write time.
        _fileSystem.File.Delete(path);
        byte[] updatedBytes = SyntheticVpxFile.Build(tableName: "Updated Content");
        _fileSystem.AddFile(path, new MockFileData(updatedBytes) { LastWriteTime = DateTime.UtcNow.AddMinutes(5) });

        ScanResult result = await ScanAsync();

        result.Scanned.Should().Be(1, "the file's size or write time changed, so it must be re-read");
        IReadOnlyList<VpxTableFile> stored = await GetAllAsync();
        stored.Should().ContainSingle().Which.DisplayTitle.Should().Be("Updated Content");
    }

    [Fact]
    public async Task A_file_deleted_since_the_last_scan_is_removed_from_the_database()
    {
        string path = AddTable("WillBeDeleted.vpx", tableName: "Will Be Deleted");
        await ScanAsync();

        _fileSystem.File.Delete(path);

        ScanResult result = await ScanAsync();

        result.Removed.Should().Be(1);
        IReadOnlyList<VpxTableFile> stored = await GetAllAsync();
        stored.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_tables_folder_returns_an_empty_result_rather_than_throwing()
    {
        ScanResult result = await ScanAsync(@"D:\DoesNotExist");

        result.TotalFilesFound.Should().Be(0);
        result.Scanned.Should().Be(0);
    }

    [Fact]
    public async Task An_empty_tables_folder_returns_a_zeroed_result()
    {
        _fileSystem.AddDirectory(TablesPath);

        ScanResult result = await ScanAsync();

        result.TotalFilesFound.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_scans_of_the_same_installation_are_serialized_not_racing()
    {
        AddTable("One.vpx", tableName: "One");
        AddTable("Two.vpx", tableName: "Two");
        AddTable("Three.vpx", tableName: "Three");

        // Both calls go through the *same* scanner instance (as they would in production, where the
        // scanner is a singleton) so the gate that serializes overlapping scans of one installation
        // is actually exercised, rather than each call getting its own gate.
        VpxLibraryScanner scanner = CreateScanner();

        Task<ScanResult> first = scanner.ScanAsync(InstallationId, TablesPath);
        Task<ScanResult> second = scanner.ScanAsync(InstallationId, TablesPath);

        ScanResult[] results = await Task.WhenAll(first, second);

        // Without the gate, two scans racing on the same rows could throw (a unique-index
        // violation, a concurrency exception) or leave duplicate/inconsistent rows behind. Serialized,
        // exactly one of the two does the real reading and the other finds everything already
        // recorded and skips it - never both reading, never neither.
        results.Sum(r => r.Scanned).Should().Be(3, "exactly one of the two scans should have done the actual reading");
        results.Sum(r => r.Skipped).Should().Be(3, "the other scan should have found everything already recorded");

        IReadOnlyList<VpxTableFile> stored = await GetAllAsync();
        stored.Should().HaveCount(3, "the database must end up with one row per table, not duplicates from a race");
    }

    [Fact]
    public async Task Progress_is_reported_for_every_file()
    {
        AddTable("One.vpx", tableName: "One");
        AddTable("Two.vpx", tableName: "Two");

        var reports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(reports.Add);

        await ScanAsync(progress: progress);

        // Progress<T> callbacks are marshalled asynchronously; give them a moment to arrive.
        await Task.Delay(50);

        reports.Should().NotBeEmpty();
        reports.Should().Contain(r => r.Total == 2);
    }

    // -------------------------------------------------------------------------------------------

    private string AddTable(string relativePath, string tableName)
    {
        string path = _fileSystem.Path.Combine(TablesPath, relativePath);
        byte[] bytes = SyntheticVpxFile.Build(tableName: tableName);
        _fileSystem.AddFile(path, new MockFileData(bytes) { LastWriteTime = DateTime.UtcNow });
        return path;
    }

    private async Task<ScanResult> ScanAsync(string? tablesPath = null, IProgress<ScanProgress>? progress = null) =>
        await CreateScanner().ScanAsync(InstallationId, tablesPath ?? TablesPath, progress);

    /// <summary>
    /// Builds one scanner wired to this test's real, in-memory SQLite database. Tests that need to
    /// prove behaviour *across* calls to the same scanner (e.g. the concurrency gate) must reuse one
    /// instance from this rather than calling <see cref="ScanAsync"/> twice, which builds a fresh
    /// scanner - and a fresh gate - every time.
    /// </summary>
    private VpxLibraryScanner CreateScanner()
    {
        var redactor = new PathRedactor("TestUser");

        NudgeDbContext dbContext = _database.CreateContext();
        var repository = new TableRepository(dbContext);

        var oleReader = new Nudge.Vpx.TableFiles.OleTableInfoReader(_fileSystem, redactor, NullLogger<Nudge.Vpx.TableFiles.OleTableInfoReader>.Instance);
        var filenameParser = new Nudge.Vpx.TableFiles.TableFilenameParser();
        ITableFileReader tableFileReader = new Nudge.Vpx.TableFiles.VpxTableFileReader(
            _fileSystem, oleReader, filenameParser, redactor, NullLogger<Nudge.Vpx.TableFiles.VpxTableFileReader>.Instance);

        return new VpxLibraryScanner(
            _fileSystem, tableFileReader, repository, redactor, NullLogger<VpxLibraryScanner>.Instance);
    }

    private async Task<IReadOnlyList<VpxTableFile>> GetAllAsync()
    {
        using NudgeDbContext dbContext = _database.CreateContext();
        return await new TableRepository(dbContext).GetAllAsync(InstallationId);
    }

    public void Dispose() => _database.Dispose();
}
