using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Vpx.Identification;

/// <summary>
/// Works out what a Visual Pinball executable is by combining four independent sources of evidence:
/// the filename, the Win32 version resource, the PE header, and the support libraries sitting next
/// to it.
///
/// No single source is trusted on its own. Architecture always comes from the PE header. Flavor
/// needs a name or version-resource signal, and gains confidence when a second source agrees. When
/// nothing agrees, the answer is Unknown, never a guess.
/// </summary>
public sealed class VpxExecutableIdentifier : IVpxExecutableIdentifier
{
    /// <summary>
    /// The first BGFX release with OpenXR VR support. Below this, a BGFX build's VR story is not
    /// something Nudge is willing to state. See docs/RESEARCH-NOTES.md section 4.1.
    /// </summary>
    private static readonly Version BgfxOpenXrMinimumVersion = new(10, 8, 1);

    private readonly IFileSystem _fileSystem;
    private readonly IPeArchitectureReader _architectureReader;
    private readonly IFileVersionInfoReader _versionReader;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<VpxExecutableIdentifier> _logger;

    public VpxExecutableIdentifier(
        IFileSystem fileSystem,
        IPeArchitectureReader architectureReader,
        IFileVersionInfoReader versionReader,
        IPathRedactor redactor,
        ILogger<VpxExecutableIdentifier> logger)
    {
        _fileSystem = fileSystem;
        _architectureReader = architectureReader;
        _versionReader = versionReader;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<Result<VpxExecutable>> IdentifyAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.File.Exists(executablePath))
        {
            return Result<VpxExecutable>.Failure(
                $"'{executablePath}' does not exist or cannot be read.");
        }

        string? folder = _fileSystem.Path.GetDirectoryName(executablePath);
        SiblingLibraries siblings = folder is null
            ? SiblingLibraries.Empty
            : SiblingLibraries.Scan(_fileSystem, folder);

        return await IdentifyAsync(executablePath, siblings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VpxExecutable>> IdentifyFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.Directory.Exists(folderPath))
        {
            return [];
        }

        // Scanned once for the whole folder rather than once per executable.
        SiblingLibraries siblings = SiblingLibraries.Scan(_fileSystem, folderPath);

        List<string> executablePaths;
        try
        {
            executablePaths = _fileSystem.Directory
                .EnumerateFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Could not list executables in {FolderPath}",
                _redactor.Redact(folderPath));
            return [];
        }

        var results = new List<VpxExecutable>(executablePaths.Count);
        foreach (string path in executablePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Result<VpxExecutable> result = await IdentifyAsync(path, siblings, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                results.Add(result.Value);
            }
        }

        // Recognised builds first, then by confidence, so the UI shows the useful ones at the top.
        return results
            .OrderByDescending(e => e.IsRecognised)
            .ThenByDescending(e => e.Confidence)
            .ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Result<VpxExecutable>> IdentifyAsync(
        string executablePath,
        SiblingLibraries siblings,
        CancellationToken cancellationToken)
    {
        string fileName = _fileSystem.Path.GetFileName(executablePath);
        var evidence = DetectionEvidence.Empty();

        // --- Source 1: the filename -----------------------------------------------------------
        ExecutableNameAnalysis nameAnalysis = ExecutableNameAnalyzer.Analyze(fileName);
        evidence.Add(
            "Filename",
            nameAnalysis.Reason,
            nameAnalysis.FlavorHint == VpxFlavor.Unknown
                ? EvidenceWeight.Informational
                : EvidenceWeight.Supporting);

        // --- Source 2: the PE header ----------------------------------------------------------
        Result<ProcessorArchitecture> architectureResult =
            await _architectureReader.ReadArchitectureAsync(executablePath, cancellationToken)
                .ConfigureAwait(false);

        ProcessorArchitecture architecture;
        if (architectureResult.IsSuccess)
        {
            architecture = architectureResult.Value;
            evidence.Add(
                "PE header",
                architecture == ProcessorArchitecture.Unknown
                    ? "The PE header records a machine type Nudge does not recognise."
                    : $"The PE header records this as {Describe(architecture)}.",
                EvidenceWeight.Decisive);

            AddArchitectureContradictionIfAny(evidence, fileName, architecture);
        }
        else
        {
            architecture = ProcessorArchitecture.Unknown;
            evidence.Add("PE header", architectureResult.Error, EvidenceWeight.Contradicting);
        }

        // --- Source 3: the Win32 version resource ----------------------------------------------
        FileVersionDetails version = await _versionReader.ReadAsync(executablePath, cancellationToken)
            .ConfigureAwait(false);

        RecordVersionEvidence(evidence, version);

        // --- Decide ------------------------------------------------------------------------------
        FlavorDecision decision = DecideFlavor(nameAnalysis, version, siblings, evidence);

        Version? parsedVersion = version.NumericFileVersion ?? ParseVersionString(version.ProductVersion)
                                                            ?? ParseVersionString(version.FileVersion);

        VrCapability vr = DeriveVrCapability(decision.Flavor, parsedVersion, siblings, evidence);

        var executable = new VpxExecutable
        {
            Path = executablePath,
            FileName = fileName,
            Flavor = decision.Flavor,
            Architecture = architecture,
            FileVersion = version.FileVersion,
            ProductVersion = version.ProductVersion,
            ProductName = version.ProductName,
            FileDescription = version.FileDescription,
            ParsedVersion = parsedVersion,
            VrCapability = vr,
            Confidence = decision.Confidence,
            LooksLikeVisualPinball = decision.LooksLikeVisualPinball,
            Evidence = evidence
        };

        _logger.LogDebug(
            "Identified {FileName} as {Flavor} {Architecture} ({Confidence} confidence) at {Path}",
            fileName,
            executable.Flavor,
            executable.Architecture,
            executable.Confidence,
            _redactor.Redact(executablePath));

        return Result<VpxExecutable>.Success(executable);
    }

    // -------------------------------------------------------------------------------------------
    // Flavor decision
    // -------------------------------------------------------------------------------------------

    private readonly record struct FlavorDecision(
        VpxFlavor Flavor,
        Confidence Confidence,
        bool LooksLikeVisualPinball);

    private static FlavorDecision DecideFlavor(
        ExecutableNameAnalysis nameAnalysis,
        FileVersionDetails version,
        SiblingLibraries siblings,
        DetectionEvidence evidence)
    {
        string searchable = version.ToSearchableText();
        bool versionSaysVisualPinball = searchable.Contains("visual pinball", StringComparison.Ordinal)
                                        || searchable.Contains("vpinball", StringComparison.Ordinal);

        bool looksLikeVisualPinball = nameAnalysis.LooksLikeVisualPinball || versionSaysVisualPinball;

        // Gate: without any Visual Pinball signal at all, this is simply somebody else's program.
        // Nudge says nothing further about it.
        if (!looksLikeVisualPinball)
        {
            evidence.Add(
                "Conclusion",
                "Nothing in the filename or version resource identifies this as Visual Pinball, so it "
                + "was not classified.",
                EvidenceWeight.Decisive);
            return new FlavorDecision(VpxFlavor.Unknown, Confidence.Unknown, false);
        }

        // A flavor keyword inside the version resource is the strongest signal available, because
        // unlike the filename it cannot be changed by renaming the file.
        VpxFlavor versionFlavor = FlavorFromVersionKeywords(searchable, version, evidence);

        VpxFlavor nameFlavor = nameAnalysis.FlavorHint;

        // Support libraries: corroboration only, never decisive on their own.
        VpxFlavor siblingFlavor = VpxFlavor.Unknown;
        if (siblings.HasBgfxLibraries)
        {
            siblingFlavor = VpxFlavor.Bgfx;
            evidence.Add("Support libraries", "BGFX libraries are present in this folder.");
        }
        else if (siblings.HasOpenVrRuntime)
        {
            siblingFlavor = VpxFlavor.OpenGL;
            evidence.Add(
                "Support libraries",
                "An openvr_api library is present in this folder, which the OpenVR-era OpenGL build "
                + "ships with.");
        }

        // Resolve.
        if (versionFlavor != VpxFlavor.Unknown && nameFlavor != VpxFlavor.Unknown)
        {
            if (versionFlavor == nameFlavor)
            {
                evidence.Add(
                    "Conclusion",
                    $"The filename and the version resource both indicate the {Describe(versionFlavor)} build.",
                    EvidenceWeight.Decisive);
                return new FlavorDecision(versionFlavor, Confidence.High, true);
            }

            evidence.Add(
                "Conclusion",
                $"The filename suggests {Describe(nameFlavor)} but the version resource suggests "
                + $"{Describe(versionFlavor)}. The version resource is harder to fake, so it wins, but "
                + "confidence is low.",
                EvidenceWeight.Contradicting);
            return new FlavorDecision(versionFlavor, Confidence.Low, true);
        }

        if (versionFlavor != VpxFlavor.Unknown)
        {
            evidence.Add(
                "Conclusion",
                $"The version resource identifies the {Describe(versionFlavor)} build.",
                EvidenceWeight.Decisive);
            return new FlavorDecision(versionFlavor, Confidence.High, true);
        }

        if (nameFlavor != VpxFlavor.Unknown)
        {
            // Filename alone. Confidence depends on whether anything else backs it up.
            bool corroborated = siblingFlavor == nameFlavor;
            bool versionConfirmsVisualPinball = versionSaysVisualPinball;

            if (corroborated && versionConfirmsVisualPinball)
            {
                evidence.Add(
                    "Conclusion",
                    $"The filename indicates the {Describe(nameFlavor)} build, the version resource "
                    + "confirms this is Visual Pinball, and matching support libraries are present.",
                    EvidenceWeight.Decisive);
                return new FlavorDecision(nameFlavor, Confidence.High, true);
            }

            if (versionConfirmsVisualPinball || corroborated)
            {
                evidence.Add(
                    "Conclusion",
                    $"The filename indicates the {Describe(nameFlavor)} build. The version resource does "
                    + "not name the flavor, so this rests mainly on the filename.",
                    EvidenceWeight.Supporting);
                return new FlavorDecision(nameFlavor, Confidence.Medium, true);
            }

            evidence.Add(
                "Conclusion",
                $"Only the filename suggests the {Describe(nameFlavor)} build. Nothing else corroborates "
                + "it, and filenames can be changed by anyone.",
                EvidenceWeight.Supporting);
            return new FlavorDecision(nameFlavor, Confidence.Low, true);
        }

        evidence.Add(
            "Conclusion",
            "This looks like part of Visual Pinball, but nothing identifies which rendering build it "
            + "is, so the flavor is reported as Unknown rather than guessed.",
            EvidenceWeight.Decisive);
        return new FlavorDecision(VpxFlavor.Unknown, Confidence.Unknown, true);
    }

    private static VpxFlavor FlavorFromVersionKeywords(
        string searchable,
        FileVersionDetails version,
        DetectionEvidence evidence)
    {
        if (searchable.Contains("bgfx", StringComparison.Ordinal))
        {
            evidence.Add("Version resource", "The version resource mentions BGFX.", EvidenceWeight.Decisive);
            return VpxFlavor.Bgfx;
        }

        if (searchable.Contains("opengl", StringComparison.Ordinal))
        {
            evidence.Add("Version resource", "The version resource mentions OpenGL.", EvidenceWeight.Decisive);
            return VpxFlavor.OpenGL;
        }

        if (searchable.Contains("directx", StringComparison.Ordinal) || searchable.Contains("direct3d", StringComparison.Ordinal))
        {
            evidence.Add("Version resource", "The version resource mentions DirectX.", EvidenceWeight.Decisive);
            return VpxFlavor.DirectX9;
        }

        // A major version of 9 is a solid statement that this is the Visual Pinball 9 line.
        if (version.NumericFileVersion is { Major: 9 })
        {
            evidence.Add(
                "Version resource",
                $"The version resource reports version {version.NumericFileVersion}, which is the "
                + "Visual Pinball 9 line.",
                EvidenceWeight.Decisive);
            return VpxFlavor.VP9Legacy;
        }

        return VpxFlavor.Unknown;
    }

    // -------------------------------------------------------------------------------------------
    // VR capability
    // -------------------------------------------------------------------------------------------

    private static VrCapability DeriveVrCapability(
        VpxFlavor flavor,
        Version? version,
        SiblingLibraries siblings,
        DetectionEvidence evidence)
    {
        switch (flavor)
        {
            case VpxFlavor.DirectX9:
                evidence.Add("VR", "The DirectX 9 build has no VR support.", EvidenceWeight.Decisive);
                return VrCapability.None;

            case VpxFlavor.VP9Legacy:
                evidence.Add("VR", "Visual Pinball 9 has no VR support.", EvidenceWeight.Decisive);
                return VrCapability.None;

            case VpxFlavor.OpenGL:
                evidence.Add(
                    "VR",
                    siblings.HasOpenVrRuntime
                        ? "The OpenGL build uses OpenVR, and an openvr_api library is present. SteamVR "
                          + "must be installed and running for VR to work."
                        : "The OpenGL build uses OpenVR. No openvr_api library was found in this folder, "
                          + "so VR may not be usable until SteamVR is installed.",
                    siblings.HasOpenVrRuntime ? EvidenceWeight.Decisive : EvidenceWeight.Supporting);
                return VrCapability.OpenVR;

            case VpxFlavor.Bgfx when version is not null && version >= BgfxOpenXrMinimumVersion:
                evidence.Add(
                    "VR",
                    $"The BGFX build reports version {version} which is {BgfxOpenXrMinimumVersion} or "
                    + "newer, so it supports OpenXR.",
                    EvidenceWeight.Decisive);
                return VrCapability.OpenXR;

            case VpxFlavor.Bgfx:
                evidence.Add(
                    "VR",
                    version is null
                        ? "This is a BGFX build but its version could not be read, so Nudge will not "
                          + "state whether it supports OpenXR."
                        : $"This BGFX build reports version {version}, which is older than "
                          + $"{BgfxOpenXrMinimumVersion}. Its VR support is not something Nudge can confirm.",
                    EvidenceWeight.Contradicting);
                return VrCapability.Unknown;

            default:
                return VrCapability.Unknown;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private static void RecordVersionEvidence(DetectionEvidence evidence, FileVersionDetails version)
    {
        if (version.IsEmpty)
        {
            evidence.Add(
                "Version resource",
                "This file carries no version information.",
                EvidenceWeight.Informational);
            return;
        }

        var parts = new List<string>();
        if (version.ProductName is not null)
        {
            parts.Add($"product name '{version.ProductName}'");
        }

        if (version.FileVersion is not null)
        {
            parts.Add($"file version '{version.FileVersion}'");
        }

        if (version.ProductVersion is not null && version.ProductVersion != version.FileVersion)
        {
            parts.Add($"product version '{version.ProductVersion}'");
        }

        if (parts.Count > 0)
        {
            evidence.Add("Version resource", "Reports " + string.Join(", ", parts) + ".");
        }
    }

    /// <summary>
    /// Notes when a filename claims a bit width the PE header disagrees with. Recorded as evidence,
    /// never used to change the reported architecture.
    /// </summary>
    private static void AddArchitectureContradictionIfAny(
        DetectionEvidence evidence,
        string fileName,
        ProcessorArchitecture architecture)
    {
        bool nameClaims64 = fileName.Contains("64", StringComparison.Ordinal);

        if (nameClaims64 && architecture == ProcessorArchitecture.X86)
        {
            evidence.Add(
                "Filename",
                $"'{fileName}' contains '64' but the file is actually 32-bit. The PE header is used; "
                + "the filename is ignored.",
                EvidenceWeight.Contradicting);
        }
        else if (!nameClaims64 && architecture == ProcessorArchitecture.X64
                 && fileName.StartsWith("vpinball", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(
                "Filename",
                $"'{fileName}' has no 64-bit marker but the file is 64-bit. The PE header is used.",
                EvidenceWeight.Informational);
        }
    }

    /// <summary>
    /// Pulls a numeric version out of a free-text version string such as "10.8.0 Final (build 2058)".
    /// </summary>
    internal static Version? ParseVersionString(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var numbers = new List<int>();
        int index = 0;

        while (index < text.Length && numbers.Count < 4)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                // Stop at the first non-digit that is not the separator between components.
                if (numbers.Count > 0 && text[index] != '.')
                {
                    break;
                }

                index++;
                continue;
            }

            int start = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }

            if (int.TryParse(text.AsSpan(start, index - start), out int value))
            {
                numbers.Add(value);
            }
            else
            {
                break;
            }
        }

        return numbers.Count switch
        {
            >= 4 => new Version(numbers[0], numbers[1], numbers[2], numbers[3]),
            3 => new Version(numbers[0], numbers[1], numbers[2]),
            2 => new Version(numbers[0], numbers[1]),
            _ => null
        };
    }

    private static string Describe(VpxFlavor flavor) => flavor switch
    {
        VpxFlavor.DirectX9 => "DirectX 9",
        VpxFlavor.OpenGL => "OpenGL",
        VpxFlavor.Bgfx => "BGFX",
        VpxFlavor.VP9Legacy => "Visual Pinball 9",
        _ => "unknown"
    };

    private static string Describe(ProcessorArchitecture architecture) => architecture switch
    {
        ProcessorArchitecture.X86 => "32-bit (x86)",
        ProcessorArchitecture.X64 => "64-bit (x64)",
        _ => "an unknown architecture"
    };
}
