using System.Text.RegularExpressions;
using Nudge.Core.Models;

namespace Nudge.Vpx.Identification;

/// <summary>
/// What an executable's <em>name</em> hints at. Only ever a hint: filenames are user-editable and
/// are never allowed to decide architecture, and only decide flavor with corroboration.
/// </summary>
/// <param name="LooksLikeVisualPinball">The name follows the VPinball* convention.</param>
/// <param name="FlavorHint">Flavor suggested by the name, or Unknown.</param>
/// <param name="Reason">Why, in words a user can read.</param>
internal readonly record struct ExecutableNameAnalysis(
    bool LooksLikeVisualPinball,
    VpxFlavor FlavorHint,
    string Reason);

/// <summary>
/// Filename conventions used by the Visual Pinball builds. See docs/RESEARCH-NOTES.md section 4.1.
/// </summary>
internal static partial class ExecutableNameAnalyzer
{
    public static ExecutableNameAnalysis Analyze(string fileName)
    {
        string stem = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;

        // Visual Pinball 9: VPinball995.exe, VPinball991.exe and friends. Checked before the
        // general VPinballX rules because the names overlap.
        Match legacy = LegacyVp9Pattern().Match(stem);
        if (legacy.Success)
        {
            string digits = legacy.Groups["version"].Value;
            if (digits.StartsWith('9'))
            {
                return new ExecutableNameAnalysis(
                    true,
                    VpxFlavor.VP9Legacy,
                    $"Name '{fileName}' follows the Visual Pinball 9 convention (VPinball{digits}).");
            }
        }

        if (!stem.StartsWith("vpinball", StringComparison.OrdinalIgnoreCase))
        {
            return new ExecutableNameAnalysis(
                false,
                VpxFlavor.Unknown,
                $"Name '{fileName}' does not follow any Visual Pinball naming convention.");
        }

        // Suffix tokens after the base name, e.g. VPinballX_GL64 -> ["GL64"].
        string[] tokens = stem.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string token in tokens.Skip(1))
        {
            if (IsFlavorToken(token, "BGFX"))
            {
                return new ExecutableNameAnalysis(
                    true,
                    VpxFlavor.Bgfx,
                    $"Name '{fileName}' carries the _{token} suffix used by the BGFX build.");
            }

            if (IsFlavorToken(token, "GL"))
            {
                return new ExecutableNameAnalysis(
                    true,
                    VpxFlavor.OpenGL,
                    $"Name '{fileName}' carries the _{token} suffix used by the OpenGL build.");
            }

            if (IsFlavorToken(token, "DX") || IsFlavorToken(token, "DX9"))
            {
                return new ExecutableNameAnalysis(
                    true,
                    VpxFlavor.DirectX9,
                    $"Name '{fileName}' carries the _{token} suffix used by the DirectX 9 build.");
            }
        }

        // No flavor suffix at all. In 10.8.x the unsuffixed VPinballX.exe is the DirectX 9 build.
        if (BareVpinballXPattern().IsMatch(stem))
        {
            return new ExecutableNameAnalysis(
                true,
                VpxFlavor.DirectX9,
                $"Name '{fileName}' has no flavor suffix; in Visual Pinball 10.8 the unsuffixed "
                + "executable is the DirectX 9 build.");
        }

        return new ExecutableNameAnalysis(
            true,
            VpxFlavor.Unknown,
            $"Name '{fileName}' looks like Visual Pinball but carries no recognised flavor suffix.");
    }

    /// <summary>
    /// True when the token is a flavor marker, optionally followed by a bit width: GL, GL64, BGFX64.
    /// </summary>
    private static bool IsFlavorToken(string token, string marker)
    {
        if (!token.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = token[marker.Length..];
        return remainder.Length == 0 || remainder.All(char.IsAsciiDigit);
    }

    /// <summary>VPinball995, VPinball99 - the Visual Pinball 9 series.</summary>
    [GeneratedRegex(@"^VPinball(?<version>\d{2,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyVp9Pattern();

    /// <summary>VPinballX or VPinballX64, with no flavor suffix.</summary>
    [GeneratedRegex(@"^VPinballX(64)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareVpinballXPattern();
}
