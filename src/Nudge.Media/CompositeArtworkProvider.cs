using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Media;

/// <summary>
/// The <see cref="IArtworkProvider"/> actually registered for the app to resolve - it does not find
/// artwork itself, it decides which of the other registered sources (vps-db, Google Images) to ask,
/// per table, and in what order.
/// </summary>
/// <remarks>
/// A table explicitly pinned to one source via <see cref="NudgeSettings.TableArtworkSourceOverrides"/>
/// ("use one scraper for some tables and another for other tables", per the maintainer's request)
/// asks only that source - an explicit choice is honoured, not silently second-guessed by falling
/// back to something else. A table with no override tries
/// <see cref="NudgeSettings.DefaultArtworkSourceName"/> first, then every other registered source in
/// turn, so one source finding nothing for a table doesn't stop another from filling it in - the
/// maintainer's stated goal of maximising how many tables end up with real artwork.
/// </remarks>
public sealed class CompositeArtworkProvider : IArtworkProvider
{
    private readonly IReadOnlyList<IArtworkProvider> _sources;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<CompositeArtworkProvider> _logger;

    public CompositeArtworkProvider(
        IEnumerable<IArtworkProvider> sources,
        ISettingsService settingsService,
        ILogger<CompositeArtworkProvider> logger)
    {
        _sources = [.. sources];
        _settingsService = settingsService;
        _logger = logger;
    }

    public string Name => "Automatic";

    public async Task<Result<ArtworkImage>> GetArtworkAsync(VpxTableFile table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (_sources.Count == 0)
        {
            return Result<ArtworkImage>.Failure("No artwork sources are registered.");
        }

        NudgeSettings settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

        foreach (IArtworkProvider source in OrderedSources(table, settings))
        {
            Result<ArtworkImage> result = await source.GetArtworkAsync(table, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return result;
            }
        }

        return Result<ArtworkImage>.Failure("No artwork found from any configured source.");
    }

    private IEnumerable<IArtworkProvider> OrderedSources(VpxTableFile table, NudgeSettings settings)
    {
        string? overrideName = settings.TableArtworkSourceOverrides
            .FirstOrDefault(kvp => string.Equals(kvp.Key, table.Path, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (overrideName is not null)
        {
            IArtworkProvider? overridden = FindByName(overrideName);
            if (overridden is not null)
            {
                // No fallback here on purpose: the user pinned this exact table to this exact
                // source, which is a stronger signal than "find anything, anywhere".
                yield return overridden;
                yield break;
            }

            _logger.LogDebug(
                "Table {Path} is pinned to unknown artwork source \"{Source}\"; falling back to the default order.",
                table.Path,
                overrideName);
        }

        IArtworkProvider? defaultSource = FindByName(settings.DefaultArtworkSourceName);
        if (defaultSource is not null)
        {
            yield return defaultSource;
        }

        foreach (IArtworkProvider source in _sources)
        {
            if (!ReferenceEquals(source, defaultSource))
            {
                yield return source;
            }
        }
    }

    private IArtworkProvider? FindByName(string name) =>
        _sources.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
