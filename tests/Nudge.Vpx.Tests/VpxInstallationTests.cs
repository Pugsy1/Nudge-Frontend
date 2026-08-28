using FluentAssertions;
using Nudge.Core.Models;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// <see cref="VpxInstallation.BestDesktopExecutable"/> decides which executable Nudge actually
/// launches for a normal (non-VR) session - see <see cref="Nudge.Vpx.Launching.LaunchEngine"/>,
/// which is Phase 5's launch engine.
/// </summary>
public sealed class VpxInstallationTests
{
    [Fact]
    public void Prefers_Bgfx_over_every_other_flavor()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX.exe", VpxFlavor.DirectX9),
            Executable("VPinballX_GL64.exe", VpxFlavor.OpenGL),
            Executable("VPinballX_BGFX.exe", VpxFlavor.Bgfx));

        installation.BestDesktopExecutable!.FileName.Should().Be("VPinballX_BGFX.exe");
    }

    [Fact]
    public void Prefers_DirectX9_over_OpenGL_since_OpenGL_can_silently_autolaunch_VR()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX_GL64.exe", VpxFlavor.OpenGL),
            Executable("VPinballX.exe", VpxFlavor.DirectX9));

        installation.BestDesktopExecutable!.FileName.Should().Be("VPinballX.exe");
    }

    [Fact]
    public void Falls_back_to_DirectX9_when_nothing_more_modern_is_available()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX.exe", VpxFlavor.DirectX9));

        installation.BestDesktopExecutable!.FileName.Should().Be("VPinballX.exe");
    }

    [Fact]
    public void Prefers_x64_over_x86_within_the_same_flavor()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX.exe", VpxFlavor.DirectX9, ProcessorArchitecture.X86),
            Executable("VPinballX64.exe", VpxFlavor.DirectX9, ProcessorArchitecture.X64));

        installation.BestDesktopExecutable!.FileName.Should().Be("VPinballX64.exe");
    }

    [Fact]
    public void Never_chooses_VP9Legacy_even_when_it_is_the_only_recognised_executable()
    {
        // VP9Legacy plays .vpt files, not .vpx - picking it to launch a scanned .vpx table would be
        // silently wrong rather than a loud failure. See docs/RESEARCH-NOTES.md.
        VpxInstallation installation = BuildInstallation(
            Executable("VPinball995.exe", VpxFlavor.VP9Legacy));

        installation.BestDesktopExecutable.Should().BeNull();
    }

    [Fact]
    public void Never_chooses_OpenGL_even_when_it_is_the_only_recognised_executable()
    {
        // Observed on a real Baller-installed machine: the OpenGL build autodetects SteamVR and
        // launches straight into VR with no command-line flag involved (AGENTS.md section 4.3), so a
        // plain "-Play" launch of it is not reliably a Desktop launch. Excluded until Phase 6 builds
        // proper VR profile control - reporting "no Desktop build" beats silently launching VR.
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX_GL64.exe", VpxFlavor.OpenGL));

        installation.BestDesktopExecutable.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_there_are_no_recognised_executables_at_all()
    {
        VpxInstallation installation = BuildInstallation();

        installation.BestDesktopExecutable.Should().BeNull();
    }

    [Fact]
    public void Ignores_unrecognised_executables_entirely()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("SomeRandomTool.exe", VpxFlavor.Unknown),
            Executable("VPinballX.exe", VpxFlavor.DirectX9));

        installation.BestDesktopExecutable!.FileName.Should().Be("VPinballX.exe");
    }

    [Fact]
    public void BestVrExecutable_prefers_OpenXR_over_OpenVR()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX_GL64.exe", VpxFlavor.OpenGL, vrCapability: VrCapability.OpenVR),
            Executable("VPinballX_BGFX64.exe", VpxFlavor.Bgfx, vrCapability: VrCapability.OpenXR));

        installation.BestVrExecutable!.FileName.Should().Be("VPinballX_BGFX64.exe");
    }

    [Fact]
    public void BestVrExecutable_falls_back_to_OpenVR_when_nothing_offers_OpenXR()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX.exe", VpxFlavor.DirectX9, vrCapability: VrCapability.None),
            Executable("VPinballX_GL64.exe", VpxFlavor.OpenGL, vrCapability: VrCapability.OpenVR));

        installation.BestVrExecutable!.FileName.Should().Be("VPinballX_GL64.exe");
    }

    [Fact]
    public void BestVrExecutable_prefers_x64_over_x86_within_the_same_capability()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX_GL.exe", VpxFlavor.OpenGL, ProcessorArchitecture.X86, VrCapability.OpenVR),
            Executable("VPinballX_GL64.exe", VpxFlavor.OpenGL, ProcessorArchitecture.X64, VrCapability.OpenVR));

        installation.BestVrExecutable!.FileName.Should().Be("VPinballX_GL64.exe");
    }

    [Fact]
    public void BestVrExecutable_is_null_when_nothing_reports_VR_capability()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX.exe", VpxFlavor.DirectX9, vrCapability: VrCapability.None),
            Executable("VPinball995.exe", VpxFlavor.VP9Legacy, vrCapability: VrCapability.None));

        installation.BestVrExecutable.Should().BeNull();
    }

    [Fact]
    public void BestVrExecutable_is_null_when_VR_capability_could_not_be_determined()
    {
        VpxInstallation installation = BuildInstallation(
            Executable("VPinballX_GL64.exe", VpxFlavor.OpenGL, vrCapability: VrCapability.Unknown));

        installation.BestVrExecutable.Should().BeNull();
    }

    private static VpxExecutable Executable(
        string fileName,
        VpxFlavor flavor,
        ProcessorArchitecture architecture = ProcessorArchitecture.X64,
        VrCapability vrCapability = VrCapability.Unknown) => new()
    {
        Path = $@"D:\VPX\{fileName}",
        FileName = fileName,
        Flavor = flavor,
        Architecture = architecture,
        VrCapability = vrCapability,
        Confidence = Confidence.High,
        Evidence = DetectionEvidence.Empty()
    };

    private static VpxInstallation BuildInstallation(params VpxExecutable[] executables) => new()
    {
        Id = "install-1",
        DisplayName = "Visual Pinball",
        RootPath = @"D:\VPX",
        TablesPath = @"D:\VPX\Tables",
        Executables = executables,
        DiscoverySource = InstallationSource.Manual,
        Confidence = Confidence.High,
        Evidence = DetectionEvidence.Empty()
    };
}
