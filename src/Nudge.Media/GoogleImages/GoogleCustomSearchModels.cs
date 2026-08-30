using System.Text.Json.Serialization;

namespace Nudge.Media.GoogleImages;

/// <summary>
/// The Google Custom Search JSON API's response shape (google.golang.org/api/customsearch/v1's
/// "Search" resource) - mapping only the fields this provider actually uses. Confirmed against
/// Google's own published schema (developers.google.com/custom-search/v1/reference/rest/v1/Search),
/// not against a live call - see docs/RESEARCH-NOTES.md for why: this source needs a user-supplied
/// API key Nudge's own development/test environment does not have.
/// </summary>
public sealed class GoogleCustomSearchResponse
{
    [JsonPropertyName("items")]
    public List<GoogleCustomSearchItem>? Items { get; set; }
}

public sealed class GoogleCustomSearchItem
{
    /// <summary>
    /// For an image search result (searchType=image, which this provider always sets), "link" is
    /// the direct URL of the image file itself - not the page it appears on. That's a separate
    /// field, "image.contextLink", which this provider has no use for.
    /// </summary>
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
