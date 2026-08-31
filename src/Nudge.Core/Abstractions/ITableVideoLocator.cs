namespace Nudge.Core.Abstractions;

/// <summary>
/// Finds the gameplay/trailer video that belongs to a table, so the library's hover preview works
/// without the user hand-assigning a file to every table one at a time.
///
/// Nudge does not ship or download videos - this only locates ones already on disk. Existing VPX
/// frontends (PinUP Popper, PinballX) and most media packs store them under well-known folder
/// layouts named after the table file, which is what this looks for.
/// </summary>
public interface ITableVideoLocator
{
    /// <summary>
    /// Returns the full path of a video matching <paramref name="vpxFilePath"/>, or null when there
    /// isn't one. Cheap enough to call on hover: it probes a small fixed set of candidate paths
    /// rather than walking the disk.
    /// </summary>
    /// <param name="vpxFilePath">Full path of the table's <c>.vpx</c> file.</param>
    /// <param name="installationRootPath">The Visual Pinball installation root, used to reach the sibling media folders frontends keep videos in.</param>
    /// <param name="displayTitle">
    /// The table's resolved title, used for the fuzzy pass. Media packs name their videos after the
    /// table ("Medieval Madness (Williams 1997).mp4"), not after whatever the user's own .vpx file
    /// happens to be called ("MedievalMadness_VPW_1.0.vpx"), so exact-filename matching alone finds
    /// nothing for most real collections.
    /// </param>
    string? Locate(string vpxFilePath, string? installationRootPath, string? displayTitle = null);
}
