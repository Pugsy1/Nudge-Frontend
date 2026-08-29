using System.Text.Json.Serialization;

namespace Nudge.Media.VpsDb;

/// <summary>
/// One table entry from vps-db's <c>db/vpsdb.json</c>. Deliberately maps only the fields Nudge
/// actually uses - the real file carries many more (designers, features, players...) that artwork
/// matching has no use for. Field names and shapes confirmed against the live file; see
/// docs/RESEARCH-NOTES.md.
/// </summary>
public sealed class VpsDbEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>Table (playfield) screenshots. Checked first - always present when this table has any image at all.</summary>
    [JsonPropertyName("tableFiles")]
    public List<VpsDbMediaFile> TableFiles { get; set; } = [];

    /// <summary>Backglass images. Checked when no table screenshot is available.</summary>
    [JsonPropertyName("b2sFiles")]
    public List<VpsDbMediaFile> B2SFiles { get; set; } = [];
}

/// <summary>
/// One file entry under a table's "tableFiles" or "b2sFiles" array. <see cref="ImgUrl"/> is null for
/// plenty of real entries (some just link out to a download page instead of hosting a preview) -
/// that is an ordinary, expected shape, not a parsing failure.
/// </summary>
public sealed class VpsDbMediaFile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("imgUrl")]
    public string? ImgUrl { get; set; }
}
