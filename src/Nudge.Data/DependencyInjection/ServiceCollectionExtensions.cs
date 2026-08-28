using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nudge.Core.Abstractions;
using Nudge.Data.Repositories;

namespace Nudge.Data.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public const string DatabaseFileName = "nudge.db";

    /// <summary>
    /// Registers the database. <paramref name="databasePath"/> is passed in rather than resolved
    /// here, so Nudge.Data does not need to know about Nudge.Vpx's environment-path resolution -
    /// the application composes the two together at startup.
    /// </summary>
    public static IServiceCollection AddNudgeData(this IServiceCollection services, string databasePath)
    {
        services.AddDbContext<NudgeDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<ITableRepository, TableRepository>();

        return services;
    }

    /// <summary>
    /// Applies any pending migrations, creating the database file if it doesn't exist yet. Called
    /// once at application startup, before anything tries to use the database.
    /// </summary>
    public static async Task MigrateNudgeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = services.CreateScope();
        await using NudgeDbContext dbContext = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
