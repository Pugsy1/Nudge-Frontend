using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nudge.Data;

/// <summary>
/// Lets the <c>dotnet ef</c> command-line tool construct a <see cref="NudgeDbContext"/> at design
/// time (for generating and applying migrations), since Nudge.Data is a class library with no
/// application host of its own to discover one from.
///
/// Not used by the running application - <c>Nudge.App</c> registers the real context through
/// <c>AddNudgeData</c>, pointed at the user's actual database file. This factory's database path is
/// only ever used while running <c>dotnet ef</c> commands from a terminal.
/// </summary>
public sealed class NudgeDbContextFactory : IDesignTimeDbContextFactory<NudgeDbContext>
{
    public NudgeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NudgeDbContext>();
        optionsBuilder.UseSqlite("Data Source=design-time-only.db");
        return new NudgeDbContext(optionsBuilder.Options);
    }
}
