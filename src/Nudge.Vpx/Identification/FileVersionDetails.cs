namespace Nudge.Vpx.Identification;

/// <summary>
/// The parts of a Win32 version resource that Nudge uses. Every field is optional, because plenty
/// of executables ship without a usable version resource at all.
/// </summary>
public sealed record FileVersionDetails
{
    public string? FileVersion { get; init; }

    public string? ProductVersion { get; init; }

    public string? ProductName { get; init; }

    public string? FileDescription { get; init; }

    public string? CompanyName { get; init; }

    /// <summary>The name the file had when it was built, before anybody renamed it.</summary>
    public string? OriginalFilename { get; init; }

    public string? InternalName { get; init; }

    /// <summary>The numeric version fields, which are more reliable than the string form.</summary>
    public Version? NumericFileVersion { get; init; }

    public static FileVersionDetails Empty { get; } = new();

    /// <summary>True when the file carried no usable version resource.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(FileVersion)
        && string.IsNullOrWhiteSpace(ProductVersion)
        && string.IsNullOrWhiteSpace(ProductName)
        && string.IsNullOrWhiteSpace(FileDescription)
        && NumericFileVersion is null;

    /// <summary>
    /// Every free-text field joined and lowercased, for cheap keyword matching. Nudge looks for
    /// words like "opengl" or "bgfx" wherever a particular build happens to put them, because the
    /// Visual Pinball builds are not consistent about which field carries the flavor.
    /// </summary>
    public string ToSearchableText() => string.Join(
        ' ',
        new[] { ProductName, FileDescription, InternalName, OriginalFilename, CompanyName, ProductVersion, FileVersion }
            .Where(s => !string.IsNullOrWhiteSpace(s)))
        .ToLowerInvariant();
}
