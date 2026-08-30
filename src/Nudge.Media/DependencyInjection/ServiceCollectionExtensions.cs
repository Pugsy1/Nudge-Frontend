using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Media.GoogleImages;
using Nudge.Media.VpsDb;

namespace Nudge.Media.DependencyInjection;

/// <summary>
/// Registers everything Nudge.Media provides. Assumes <c>AddNudgeVpx</c> has already been called on
/// the same collection - <see cref="IFileSystem"/>, <see cref="IPathRedactor"/> and
/// <see cref="ISettingsService"/> are all resolved here but registered there, the same assumption
/// <c>Nudge.Data</c>'s <c>AddNudgeData</c> already makes.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="artworkCacheDirectory">
    /// Where the vps-db index and resolved per-table images are cached, e.g.
    /// <c>%LocalAppData%\Nudge\artwork</c>. Passed in rather than resolved here, the same reasoning
    /// <c>AddNudgeData</c> uses for its database path - Nudge.Media does not need to know about
    /// Nudge.Vpx's environment-path resolution.
    /// </param>
    public static IServiceCollection AddNudgeMedia(this IServiceCollection services, string artworkCacheDirectory)
    {
        services.AddHttpClient();

        services.TryAddSingleton<IVpsDbIndex>(provider =>
        {
            HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(VpsDbIndex));
            string cacheFilePath = provider.GetRequiredService<IFileSystem>().Path.Combine(artworkCacheDirectory, "vpsdb-index.json");

            return new VpsDbIndex(
                client,
                provider.GetRequiredService<IFileSystem>(),
                cacheFilePath,
                provider.GetRequiredService<IPathRedactor>(),
                provider.GetRequiredService<ILogger<VpsDbIndex>>());
        });

        services.TryAddSingleton<IArtworkCache>(provider =>
        {
            IFileSystem fileSystem = provider.GetRequiredService<IFileSystem>();
            string imagesDirectory = fileSystem.Path.Combine(artworkCacheDirectory, "images");

            return new ArtworkCache(
                fileSystem,
                imagesDirectory,
                provider.GetRequiredService<IPathRedactor>(),
                provider.GetRequiredService<ILogger<ArtworkCache>>());
        });

        // Each concrete source is registered under its own type - not IArtworkProvider - so it can
        // be resolved individually below without also satisfying every other IArtworkProvider
        // consumer in the container. IArtworkProvider itself resolves to CompositeArtworkProvider,
        // the only thing the rest of the app ever needs to know about.
        services.TryAddSingleton<VpsDbArtworkProvider>(provider =>
        {
            HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(VpsDbArtworkProvider));

            return new VpsDbArtworkProvider(
                provider.GetRequiredService<IVpsDbIndex>(),
                provider.GetRequiredService<IArtworkCache>(),
                client,
                provider.GetRequiredService<ISettingsService>(),
                provider.GetRequiredService<IPathRedactor>(),
                provider.GetRequiredService<ILogger<VpsDbArtworkProvider>>());
        });

        services.TryAddSingleton<GoogleCustomSearchArtworkProvider>(provider =>
        {
            HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleCustomSearchArtworkProvider));

            return new GoogleCustomSearchArtworkProvider(
                provider.GetRequiredService<IArtworkCache>(),
                client,
                provider.GetRequiredService<ISettingsService>(),
                provider.GetRequiredService<IPathRedactor>(),
                provider.GetRequiredService<ILogger<GoogleCustomSearchArtworkProvider>>());
        });

        services.TryAddSingleton<IArtworkProvider>(provider => new CompositeArtworkProvider(
            [
                provider.GetRequiredService<VpsDbArtworkProvider>(),
                provider.GetRequiredService<GoogleCustomSearchArtworkProvider>()
            ],
            provider.GetRequiredService<ISettingsService>(),
            provider.GetRequiredService<ILogger<CompositeArtworkProvider>>()));

        services.TryAddSingleton<IArtworkBrowser>(provider => new ArtworkBrowser(
            [
                provider.GetRequiredService<VpsDbArtworkProvider>(),
                provider.GetRequiredService<GoogleCustomSearchArtworkProvider>()
            ]));

        return services;
    }
}
