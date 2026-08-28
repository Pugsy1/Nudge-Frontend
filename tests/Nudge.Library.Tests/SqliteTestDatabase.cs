using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Data;

namespace Nudge.Library.Tests;

/// <summary>A real, in-memory SQLite database for one test. See the identical helper in
/// Nudge.Data.Tests for why real SQLite is used instead of EF Core's InMemory provider.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using NudgeDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    public NudgeDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NudgeDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
