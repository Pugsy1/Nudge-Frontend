using Microsoft.EntityFrameworkCore;
using Nudge.Data.Entities;

namespace Nudge.Data;

public sealed class NudgeDbContext : DbContext
{
    public NudgeDbContext(DbContextOptions<NudgeDbContext> options) : base(options)
    {
    }

    public DbSet<TableEntity> Tables => Set<TableEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TableEntity>(table =>
        {
            table.ToTable("Tables");

            table.HasKey(t => t.Id);

            table.Property(t => t.InstallationId).IsRequired();
            table.Property(t => t.FilePath).IsRequired();
            table.Property(t => t.FileName).IsRequired();
            table.Property(t => t.DisplayTitle).IsRequired();
            table.Property(t => t.Confidence).IsRequired();

            // One row per file per installation - the scanner upserts by this pair.
            table.HasIndex(t => new { t.InstallationId, t.FilePath }).IsUnique();

            // The grid will filter/sort by title constantly once it exists (Phase 4); index it now
            // so that query is fast from day one instead of needing a follow-up migration.
            table.HasIndex(t => t.DisplayTitle);
        });
    }
}
