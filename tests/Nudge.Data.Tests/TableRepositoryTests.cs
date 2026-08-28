using FluentAssertions;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Data.Repositories;
using Xunit;

namespace Nudge.Data.Tests;

public sealed class TableRepositoryTests : IDisposable
{
    private const string InstallationId = "install-1";

    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task A_table_can_be_saved_and_read_back()
    {
        await using NudgeDbContext writeContext = _database.CreateContext();
        var repository = new TableRepository(writeContext);

        VpxTableFile table = BuildTable(@"D:\Tables\Medieval Madness.vpx", "Medieval Madness", "Williams", 1997);
        await repository.UpsertAsync(InstallationId, new TableScanEntry(table, 12_345, DateTimeOffset.UtcNow));

        await using NudgeDbContext readContext = _database.CreateContext();
        var readRepository = new TableRepository(readContext);
        IReadOnlyList<VpxTableFile> all = await readRepository.GetAllAsync(InstallationId);

        all.Should().ContainSingle();
        all[0].DisplayTitle.Should().Be("Medieval Madness");
        all[0].DisplayManufacturer.Should().Be("Williams");
        all[0].DisplayYear.Should().Be(1997);
        all[0].Confidence.Should().Be(Confidence.High);
    }

    [Fact]
    public async Task Upserting_the_same_path_again_updates_rather_than_duplicating()
    {
        await using NudgeDbContext context = _database.CreateContext();
        var repository = new TableRepository(context);

        VpxTableFile first = BuildTable(@"D:\Tables\Table.vpx", "Original Title");
        await repository.UpsertAsync(InstallationId, new TableScanEntry(first, 100, DateTimeOffset.UtcNow));

        VpxTableFile updated = BuildTable(@"D:\Tables\Table.vpx", "Updated Title");
        await repository.UpsertAsync(InstallationId, new TableScanEntry(updated, 200, DateTimeOffset.UtcNow));

        IReadOnlyList<VpxTableFile> all = await repository.GetAllAsync(InstallationId);

        all.Should().ContainSingle();
        all[0].DisplayTitle.Should().Be("Updated Title");
    }

    [Fact]
    public async Task Evidence_and_filename_tags_round_trip_through_JSON_storage()
    {
        await using NudgeDbContext writeContext = _database.CreateContext();
        var repository = new TableRepository(writeContext);

        var evidence = DetectionEvidence.Empty();
        evidence.Add("TableInfo", "The table's own metadata names it 'Foo'.", EvidenceWeight.Supporting);
        evidence.Add("Filename", "The filename suggests 'Bar'.", EvidenceWeight.Contradicting);

        var table = new VpxTableFile
        {
            Path = @"D:\Tables\Evidenced.vpx",
            FileName = "Evidenced.vpx",
            FileSizeBytes = 42,
            TableInfo = TableInfoMetadata.Empty,
            FilenameHints = new FilenameHints { Title = "Bar", Tags = ["MOD", "1.2"] },
            DisplayTitle = "Bar",
            Confidence = Confidence.Medium,
            Evidence = evidence
        };

        await repository.UpsertAsync(InstallationId, new TableScanEntry(table, 42, DateTimeOffset.UtcNow));

        await using NudgeDbContext readContext = _database.CreateContext();
        VpxTableFile roundTripped = (await new TableRepository(readContext).GetAllAsync(InstallationId))[0];

        roundTripped.FilenameHints.Tags.Should().Equal("MOD", "1.2");
        roundTripped.Evidence.Should().HaveCount(2);
        roundTripped.Evidence.Summary.Should().Contain("Foo").And.Contain("Bar");
    }

    [Fact]
    public async Task Fingerprints_reflect_the_size_and_last_write_time_that_were_saved()
    {
        await using NudgeDbContext context = _database.CreateContext();
        var repository = new TableRepository(context);

        var lastWrite = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        VpxTableFile table = BuildTable(@"D:\Tables\Fingerprinted.vpx", "Fingerprinted");
        await repository.UpsertAsync(InstallationId, new TableScanEntry(table, 999, lastWrite));

        IReadOnlyDictionary<string, ScannedFileFingerprint> fingerprints =
            await repository.GetFingerprintsAsync(InstallationId);

        fingerprints.Should().ContainKey(@"D:\Tables\Fingerprinted.vpx");
        ScannedFileFingerprint fingerprint = fingerprints[@"D:\Tables\Fingerprinted.vpx"];
        fingerprint.FileSizeBytes.Should().Be(999);
        fingerprint.FileLastWriteTimeUtc.Should().Be(lastWrite);
    }

    [Fact]
    public async Task DeleteMissing_removes_only_paths_not_in_the_current_set()
    {
        await using NudgeDbContext context = _database.CreateContext();
        var repository = new TableRepository(context);

        await repository.UpsertAsync(InstallationId, new TableScanEntry(BuildTable(@"D:\Tables\Keep.vpx", "Keep"), 1, DateTimeOffset.UtcNow));
        await repository.UpsertAsync(InstallationId, new TableScanEntry(BuildTable(@"D:\Tables\Gone.vpx", "Gone"), 1, DateTimeOffset.UtcNow));

        int removed = await repository.DeleteMissingAsync(InstallationId, new HashSet<string> { @"D:\Tables\Keep.vpx" });

        removed.Should().Be(1);
        IReadOnlyList<VpxTableFile> remaining = await repository.GetAllAsync(InstallationId);
        remaining.Should().ContainSingle().Which.DisplayTitle.Should().Be("Keep");
    }

    [Fact]
    public async Task Tables_are_scoped_to_their_installation()
    {
        await using NudgeDbContext context = _database.CreateContext();
        var repository = new TableRepository(context);

        await repository.UpsertAsync("install-A", new TableScanEntry(BuildTable(@"D:\A\Table.vpx", "In A"), 1, DateTimeOffset.UtcNow));
        await repository.UpsertAsync("install-B", new TableScanEntry(BuildTable(@"D:\B\Table.vpx", "In B"), 1, DateTimeOffset.UtcNow));

        IReadOnlyList<VpxTableFile> aOnly = await repository.GetAllAsync("install-A");

        aOnly.Should().ContainSingle().Which.DisplayTitle.Should().Be("In A");
    }

    [Fact]
    public async Task UpsertMany_saves_a_whole_batch_in_one_call()
    {
        await using NudgeDbContext context = _database.CreateContext();
        var repository = new TableRepository(context);

        var entries = Enumerable.Range(0, 50)
            .Select(i => new TableScanEntry(
                BuildTable($@"D:\Tables\Table{i}.vpx", $"Table {i}"),
                i,
                DateTimeOffset.UtcNow))
            .ToList();

        await repository.UpsertManyAsync(InstallationId, entries);

        IReadOnlyList<VpxTableFile> all = await repository.GetAllAsync(InstallationId);
        all.Should().HaveCount(50);
    }

    [Fact]
    public async Task An_empty_batch_does_nothing_rather_than_throwing()
    {
        await using NudgeDbContext context = _database.CreateContext();
        var repository = new TableRepository(context);

        await repository.UpsertManyAsync(InstallationId, []);

        IReadOnlyList<VpxTableFile> all = await repository.GetAllAsync(InstallationId);
        all.Should().BeEmpty();
    }

    private static VpxTableFile BuildTable(
        string path,
        string displayTitle,
        string? manufacturer = null,
        int? year = null)
    {
        return new VpxTableFile
        {
            Path = path,
            FileName = System.IO.Path.GetFileName(path),
            FileSizeBytes = 0,
            TableInfo = TableInfoMetadata.Empty,
            FilenameHints = FilenameHints.Empty,
            DisplayTitle = displayTitle,
            DisplayManufacturer = manufacturer,
            DisplayYear = year,
            Confidence = manufacturer is not null ? Confidence.High : Confidence.Medium,
            Evidence = DetectionEvidence.Empty()
        };
    }

    public void Dispose() => _database.Dispose();
}
