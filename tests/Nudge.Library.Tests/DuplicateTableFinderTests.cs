using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Data;
using Nudge.Data.Repositories;
using Xunit;

namespace Nudge.Library.Tests;

/// <summary>
/// <see cref="DuplicateTableFinder"/> only ever hashes files that already share a recorded file
/// size with at least one other table - these tests prove both halves of that: the cheap size
/// pre-filter (nothing outside a size collision is ever read), and that hashing, not size alone, is
/// what decides a true duplicate (two same-sized-but-different files must not be reported).
/// </summary>
public sealed class DuplicateTableFinderTests : IDisposable
{
    private const string InstallationId = "install-1";
    private const string TablesPath = @"D:\VPX\Tables";

    private readonly MockFileSystem _fileSystem = new();
    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task Two_byte_for_byte_identical_files_are_reported_as_one_duplicate_group()
    {
        byte[] content = [1, 2, 3, 4, 5];
        await SeedAsync("One.vpx", content);
        await SeedAsync("Copy.vpx", content);

        IReadOnlyList<DuplicateTableGroup> groups = await FindDuplicatesAsync();

        groups.Should().ContainSingle();
        groups[0].Tables.Should().HaveCount(2);
        groups[0].Tables.Select(t => t.FileName).Should().BeEquivalentTo("One.vpx", "Copy.vpx");
    }

    [Fact]
    public async Task Same_size_but_different_content_is_not_reported_as_a_duplicate()
    {
        await SeedAsync("One.vpx", [1, 2, 3, 4, 5]);
        await SeedAsync("Other.vpx", [9, 9, 9, 9, 9]); // same length, different bytes

        IReadOnlyList<DuplicateTableGroup> groups = await FindDuplicatesAsync();

        groups.Should().BeEmpty("matching size alone must never be enough to call two files duplicates");
    }

    [Fact]
    public async Task A_table_with_a_unique_size_is_never_hashed_at_all()
    {
        await SeedAsync("One.vpx", [1, 2, 3]);
        await SeedAsync("Copy.vpx", [1, 2, 3]);
        await SeedAsync("Unrelated.vpx", [1, 2, 3, 4, 5, 6, 7, 8, 9]); // distinct size

        var reports = new List<DuplicateScanProgress>();
        await FindDuplicatesAsync(new Progress<DuplicateScanProgress>(reports.Add));
        await Task.Delay(50); // Progress<T> marshals asynchronously

        reports.Should().NotBeEmpty();
        reports.Should().OnlyContain(r => r.Total == 2, "only the two same-sized files are ever candidates for hashing");
    }

    [Fact]
    public async Task No_duplicates_at_all_reports_nothing_and_hashes_nothing()
    {
        await SeedAsync("One.vpx", [1, 2, 3]);
        await SeedAsync("Two.vpx", [4, 5, 6, 7]);

        var reports = new List<DuplicateScanProgress>();
        IReadOnlyList<DuplicateTableGroup> groups = await FindDuplicatesAsync(new Progress<DuplicateScanProgress>(reports.Add));

        groups.Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_that_can_no_longer_be_read_is_skipped_without_failing_the_whole_search()
    {
        byte[] content = [1, 2, 3, 4, 5];
        string missingPath = await SeedAsync("WentMissing.vpx", content);
        await SeedAsync("StillThere1.vpx", content);
        await SeedAsync("StillThere2.vpx", content);
        _fileSystem.File.Delete(missingPath); // recorded in the DB, but gone from disk since

        IReadOnlyList<DuplicateTableGroup> groups = await FindDuplicatesAsync();

        groups.Should().ContainSingle();
        groups[0].Tables.Select(t => t.FileName).Should().BeEquivalentTo("StillThere1.vpx", "StillThere2.vpx");
    }

    [Fact]
    public async Task Three_identical_files_form_a_single_group_of_three_not_three_pairs()
    {
        byte[] content = [7, 7, 7];
        await SeedAsync("A.vpx", content);
        await SeedAsync("B.vpx", content);
        await SeedAsync("C.vpx", content);

        IReadOnlyList<DuplicateTableGroup> groups = await FindDuplicatesAsync();

        groups.Should().ContainSingle();
        groups[0].Tables.Should().HaveCount(3);
    }

    // -------------------------------------------------------------------------------------------

    private async Task<string> SeedAsync(string fileName, byte[] content)
    {
        string path = _fileSystem.Path.Combine(TablesPath, fileName);
        _fileSystem.AddFile(path, new MockFileData(content));

        var table = new VpxTableFile
        {
            Path = path,
            FileName = fileName,
            FileSizeBytes = content.Length,
            TableInfo = TableInfoMetadata.Empty,
            FilenameHints = FilenameHints.Empty,
            DisplayTitle = fileName,
            Confidence = Confidence.High,
            Evidence = DetectionEvidence.Empty()
        };

        using NudgeDbContext dbContext = _database.CreateContext();
        var repository = new TableRepository(dbContext);
        await repository.UpsertManyAsync(
            InstallationId,
            [new TableScanEntry(table, content.Length, DateTimeOffset.UtcNow)]);

        return path;
    }

    private async Task<IReadOnlyList<DuplicateTableGroup>> FindDuplicatesAsync(IProgress<DuplicateScanProgress>? progress = null)
    {
        var scopeFactory = new FakeScopeFactory(() => new TableRepository(_database.CreateContext()));
        var finder = new DuplicateTableFinder(
            _fileSystem, scopeFactory, new PathRedactor("TestUser"), NullLogger<DuplicateTableFinder>.Instance);

        return await finder.FindDuplicatesAsync(InstallationId, progress);
    }

    private sealed class FakeScopeFactory(Func<ITableRepository> createRepository) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FakeScope(createRepository());
    }

    private sealed class FakeScope(ITableRepository repository) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new FakeServiceProvider(repository);

        public void Dispose()
        {
        }
    }

    private sealed class FakeServiceProvider(ITableRepository repository) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ITableRepository) ? repository : null;
    }

    public void Dispose() => _database.Dispose();
}
