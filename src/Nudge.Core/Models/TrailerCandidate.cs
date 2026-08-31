namespace Nudge.Core.Models;

/// <summary>
/// One video offered by a trailer search, for the user to pick between on a table's customization
/// page. Carries only what the picker needs to show a choice - the id to play, a title to read, and
/// a still to look at.
/// </summary>
/// <param name="VideoId">The bare YouTube video id, which is what gets stored and embedded.</param>
/// <param name="Title">The video's own title, so several results for one table are distinguishable.</param>
public sealed record TrailerCandidate(string VideoId, string Title)
{
    /// <summary>
    /// YouTube's published still for the video. Built from the id rather than carried through from
    /// the source data, because vps-db stores only the id - and this URL form is stable and needs no
    /// API key, which is why the picker can show real thumbnails without any extra configuration.
    /// </summary>
    public string ThumbnailUrl => $"https://img.youtube.com/vi/{VideoId}/hqdefault.jpg";
}
