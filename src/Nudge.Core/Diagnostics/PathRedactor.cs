using System.Text.RegularExpressions;

namespace Nudge.Core.Diagnostics;

/// <summary>
/// Default redactor. Pure string work, no I/O, so it lives in Core and is trivially testable.
/// </summary>
public sealed partial class PathRedactor : IPathRedactor
{
    /// <summary>What the username is replaced with. Deliberately obvious in a log.</summary>
    public const string Placeholder = "<user>";

    /// <summary>
    /// Usernames shorter than this are not redacted on their own. A two-letter username would match
    /// inside ordinary words and turn logs into nonsense; the path-anchored rule still covers it.
    /// </summary>
    private const int MinimumRedactableUserNameLength = 3;

    private readonly Regex? _bareUserNamePattern;
    private readonly Regex? _profilePathPattern;

    /// <param name="userName">The Windows account name, normally Environment.UserName.</param>
    /// <param name="userProfilePath">
    /// The account's profile folder, normally Environment.GetFolderPath(UserProfile). Redacted as a
    /// whole because domain and roaming profiles do not always sit under \Users.
    /// </param>
    public PathRedactor(string? userName, string? userProfilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(userName) && userName.Length >= MinimumRedactableUserNameLength)
        {
            // \b would not fire next to a backslash, so bound on characters that cannot be part of a
            // Windows account name instead.
            _bareUserNamePattern = new Regex(
                $@"(?<![A-Za-z0-9]){Regex.Escape(userName)}(?![A-Za-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!string.IsNullOrWhiteSpace(userProfilePath))
        {
            _profilePathPattern = new Regex(
                Regex.Escape(userProfilePath.TrimEnd('\\', '/')),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Longest, most specific match first: the whole profile path.
        string result = _profilePathPattern is null
            ? text
            : _profilePathPattern.Replace(text, $@"C:\Users\{Placeholder}");

        // Then anything shaped like \Users\someone, which catches other accounts on the machine too.
        result = UsersFolderPattern().Replace(result, $"${{sep}}Users${{sep2}}{Placeholder}");

        // Finally the bare username wherever else it appears.
        if (_bareUserNamePattern is not null)
        {
            result = _bareUserNamePattern.Replace(result, Placeholder);
        }

        return result;
    }

    /// <summary>Matches "\Users\name" or "/Users/name", capturing the separators actually used.</summary>
    [GeneratedRegex(@"(?<sep>[\\/])Users(?<sep2>[\\/])(?<name>[^\\/\r\n""]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsersFolderPattern();
}
