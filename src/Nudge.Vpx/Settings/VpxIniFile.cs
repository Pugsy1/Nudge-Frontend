using System.IO.Abstractions;

namespace Nudge.Vpx.Settings;

/// <summary>
/// A parsed Visual Pinball settings file.
///
/// Nudge only ever <em>reads</em> the user's VPinballX.ini. Phase 1 writes nothing at all, and later
/// phases will only write ini files inside Nudge's own data directory. See AGENTS.md section 6.
/// </summary>
public sealed class VpxIniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections;

    private VpxIniFile(Dictionary<string, Dictionary<string, string>> sections) => _sections = sections;

    public IReadOnlyCollection<string> SectionNames => _sections.Keys;

    public static VpxIniFile Empty { get; } = new(new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Reads and parses an ini file. Returns <see cref="Empty"/> when the file is missing or
    /// unreadable, because a missing settings file is a normal state, not an error.
    /// </summary>
    public static async Task<VpxIniFile> ReadAsync(
        IFileSystem fileSystem,
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!fileSystem.File.Exists(path))
            {
                return Empty;
            }

            string[] lines = await fileSystem.File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            return Parse(lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static VpxIniFile Parse(IEnumerable<string> lines)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sections[string.Empty] = current;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                string sectionName = line[1..^1].Trim();
                if (!sections.TryGetValue(sectionName, out current!))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections[sectionName] = current;
                }

                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();

            // Values are sometimes quoted, sometimes not.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            if (key.Length > 0)
            {
                current[key] = value;
            }
        }

        return new VpxIniFile(sections);
    }

    /// <summary>Reads a key from a named section.</summary>
    public string? GetValue(string section, string key) =>
        _sections.TryGetValue(section, out Dictionary<string, string>? values)
        && values.TryGetValue(key, out string? value)
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>
    /// Finds a key in whichever section happens to hold it.
    /// </summary>
    /// <remarks>
    /// Visual Pinball has moved settings between sections across versions, and Nudge supports more
    /// than one version. Searching every section is more robust than hard-coding a section name
    /// that is right for 10.8.0 and wrong for the next release.
    /// </remarks>
    public string? FindValue(string key)
    {
        foreach (Dictionary<string, string> values in _sections.Values)
        {
            if (values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>The directory hints Visual Pinball records, in the order Nudge trusts them.</summary>
    public IEnumerable<(string Key, string Value)> GetDirectoryHints()
    {
        foreach (string key in (string[])["TablesDirectory", "ScriptsDirectory", "MusicDirectory"])
        {
            string? value = FindValue(key);
            if (value is not null)
            {
                yield return (key, value);
            }
        }
    }
}
