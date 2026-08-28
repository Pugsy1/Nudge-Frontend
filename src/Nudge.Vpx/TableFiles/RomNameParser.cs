using System.Text.RegularExpressions;
using Nudge.Core.Models;

namespace Nudge.Vpx.TableFiles;

/// <summary>Finds a PinMAME ROM name inside a table's VBScript source text.</summary>
public interface IRomNameParser
{
    RomNameInfo Parse(string script);
}

/// <summary>
/// The community convention, confirmed against real tables (see docs/RESEARCH-NOTES.md), is a
/// top-level <c>cGameName</c> assignment somewhere in the script, usually as
/// <c>Const cGameName = "romname"</c>. This is a plain text search, not a VBScript parser or
/// interpreter - Nudge never executes a table's script.
/// </summary>
public sealed class RomNameParser : IRomNameParser
{
    // Anchored to the start of a line (ignoring leading whitespace): a commented-out line always
    // starts with "'" and so never matches, which is what lets a real table with several
    // commented-out alternative ROM revisions and one live one resolve cleanly. A table that instead
    // assigns cGameName conditionally (e.g. "Case 0: cGameName = ...") also does not match, since the
    // line does not start with "Const" or "cGameName" - that is reported as "not found" rather than
    // guessed at, since Nudge cannot evaluate which branch would run.
    private static readonly Regex AssignmentPattern = new(
        @"^[ \t]*(?:Const[ \t]+)?cGameName[ \t]*=[ \t]*""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public RomNameInfo Parse(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return RomNameInfo.NotFound(DetectionEvidence.Empty()
                .Add("Script", "No script text was available to search.", EvidenceWeight.Informational));
        }

        List<string> matches = AssignmentPattern.Matches(script)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DetectionEvidence evidence = DetectionEvidence.Empty();

        if (matches.Count == 0)
        {
            evidence.Add(
                "Script",
                "No top-level \"cGameName\" assignment was found. The table may set it conditionally "
                + "(e.g. inside a Select Case block), which Nudge does not evaluate.",
                EvidenceWeight.Informational);
            return RomNameInfo.NotFound(evidence);
        }

        if (matches.Count == 1)
        {
            evidence.Add(
                "Script",
                $"Found a single \"cGameName\" assignment: \"{matches[0]}\".",
                EvidenceWeight.Decisive);

            return new RomNameInfo
            {
                RomName = matches[0],
                Confidence = Confidence.High,
                Evidence = evidence
            };
        }

        evidence.Add(
            "Script",
            $"Found {matches.Count} different uncommented \"cGameName\" assignments: "
            + string.Join(", ", matches.Select(m => $"\"{m}\"")) + ". Using the first.",
            EvidenceWeight.Contradicting);

        return new RomNameInfo
        {
            RomName = matches[0],
            Confidence = Confidence.Medium,
            Evidence = evidence
        };
    }
}
