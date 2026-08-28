using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Nudge.Data.Tests;

/// <summary>
/// A real SQLite database, in memory, for the duration of one test. Real SQLite rather than EF
/// Core's separate InMemory provider on purpose: the InMemory provider does not enforce
/// constraints or behave identically to a real relational engine, so a passing test against it
/// would not actually prove the schema or the repository's queries work against SQLite.
///
/// A `:memory:` SQLite database only exists for as long as its connection stays open, so the
/// connection is held open here and disposed with the fixture.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = new NudgeDbContext(BuildOptions());
        context.Database.EnsureCreated();
    }

    public NudgeDbContext CreateContext() => new(BuildOptions());

    private DbContextOptions<NudgeDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<NudgeDbContext>().UseSqlite(_connection).Options;

    public void Dispose() => _connection.Dispose();
}
