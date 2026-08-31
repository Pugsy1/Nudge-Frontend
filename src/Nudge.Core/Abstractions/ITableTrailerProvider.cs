using Nudge.Core.Models;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Finds an online video for a table, for the library's hover preview.
///
/// Returns a YouTube video id rather than a file: these videos are played through YouTube's own
/// embedded player, never downloaded. Downloading them would breach YouTube's Terms of Service, and
/// no source publishes pinball gameplay video in a form Nudge could legitimately fetch and cache.
/// </summary>
public interface ITableTrailerProvider
{
    /// <summary>
    /// The YouTube video id for <paramref name="table"/>, or null when there isn't one - which is
    /// the majority case, since only about one table in nine in vps-db carries a video at all.
    /// </summary>
    Task<string?> GetYouTubeVideoIdAsync(VpxTableFile table, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every video known for <paramref name="table"/>, for the picker on its customization page.
    /// Empty when the table has none, which the caller is expected to report plainly rather than
    /// leave looking like a failed search.
    ///
    /// <para>Separate from <see cref="GetYouTubeVideoIdAsync"/> because they answer different
    /// questions: that one is "what should hover play right now", used automatically and cached
    /// hard; this one is "show me everything so I can choose", run only when the user asks.</para>
    /// </summary>
    Task<IReadOnlyList<TrailerCandidate>> FindTrailersAsync(VpxTableFile table, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads whatever backing data the lookups need, so the first one doesn't pay for it.
    ///
    /// Without this the first hover of a session triggers a multi-megabyte index download and shows
    /// nothing, because the pointer has long since moved on by the time it lands - the preview only
    /// starts working from the second or third table onwards, which reads as broken rather than
    /// slow. Safe to call repeatedly and safe to ignore the result.
    /// </summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}
