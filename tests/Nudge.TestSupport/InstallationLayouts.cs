using System.IO.Abstractions.TestingHelpers;
using Nudge.Core.Models;
using Nudge.Vpx.Identification;

namespace Nudge.TestSupport;

/// <summary>
/// A synthetic Visual Pinball installation on an in-memory disk, together with the scripted version
/// resources that go with it.
/// </summary>
public sealed class SyntheticInstallation
{
    public required MockFileSystem FileSystem { get; init; }

    /// <summary>The folder that should be detected as the installation root.</summary>
    public required string RootPath { get; init; }

    public required FakeFileVersionInfoReader VersionInfo { get; init; }

    public required FakeEnvironmentPaths Environment { get; init; }

    /// <summary>Full path to a file inside the installation root.</summary>
    public string PathTo(string relative) => FileSystem.Path.Combine(RootPath, relative);
}

/// <summary>
/// The installation shapes Nudge has to cope with, built on an in-memory filesystem.
///
/// These deliberately use the D: drive rather than C:, and a nonstandard user name, so that nothing
/// accidentally passes because it matched the machine the tests happen to run on.
/// </summary>
public static class InstallationLayouts
{
    private const string DefaultRoot = @"D:\vPinball\VisualPinball";

    /// <summary>
    /// The layout produced by the Baller Installer, which is what the maintainer runs: Visual Pinball
    /// 10.8.0 with DirectX 9 and OpenGL builds in both bit widths, the legacy Visual Pinball 9
    /// executable alongside, VPinMAME and B2S, and a populated Tables folder.
    /// </summary>
    public static SyntheticInstallation Baller(string root = DefaultRoot)
    {
        var fileSystem = new MockFileSystem();
        var versions = new FakeFileVersionInfoReader();

        fileSystem.AddDirectory(root);

        // Visual Pinball 10.8 builds. Architectures deliberately do not all match their filenames'
        // implications, so tests can confirm the PE header is what is being read.
        AddExecutable(fileSystem, versions, root, "VPinballX.exe", ProcessorArchitecture.X86);
        AddExecutable(fileSystem, versions, root, "VPinballX64.exe", ProcessorArchitecture.X64);
        AddExecutable(fileSystem, versions, root, "VPinballX_GL.exe", ProcessorArchitecture.X86);
        AddExecutable(fileSystem, versions, root, "VPinballX_GL64.exe", ProcessorArchitecture.X64);

        // Visual Pinball 9, which Baller installs alongside for .vpt tables.
        string legacyPath = fileSystem.Path.Combine(root, "VPinball995.exe");
        fileSystem.AddFile(legacyPath, new MockFileData(SyntheticPortableExecutable.X86()));
        versions.Set(legacyPath, new FileVersionDetails
        {
            ProductName = "Visual Pinball",
            FileDescription = "Visual Pinball",
            FileVersion = "9.9.5.0",
            ProductVersion = "9.9.5",
            NumericFileVersion = new Version(9, 9, 5, 0)
        });

        // Something that is emphatically not Visual Pinball, to prove it is left alone.
        string uninstaller = fileSystem.Path.Combine(root, "unins000.exe");
        fileSystem.AddFile(uninstaller, new MockFileData(SyntheticPortableExecutable.X86()));
        versions.Set(uninstaller, new FileVersionDetails
        {
            ProductName = "Setup",
            FileDescription = "Setup/Uninstall",
            CompanyName = "Jordan Russell",
            FileVersion = "51.1052.0.0",
            NumericFileVersion = new Version(51, 1052, 0, 0)
        });

        // Support libraries that corroborate the OpenGL build's OpenVR capability.
        fileSystem.AddFile(fileSystem.Path.Combine(root, "openvr_api64.dll"), new MockFileData([1, 2, 3]));
        fileSystem.AddFile(fileSystem.Path.Combine(root, "VPinMAME.dll"), new MockFileData([1, 2, 3]));
        fileSystem.AddFile(fileSystem.Path.Combine(root, "B2SBackglassServer.dll"), new MockFileData([1, 2, 3]));

        // A populated tables folder.
        fileSystem.AddDirectory(fileSystem.Path.Combine(root, "Tables"));
        fileSystem.AddFile(
            fileSystem.Path.Combine(root, "Tables", "Some Table (Manufacturer 1985).vpx"),
            new MockFileData([0xD0, 0xCF, 0x11, 0xE0]));

        fileSystem.AddDirectory(fileSystem.Path.Combine(root, "VPinMAME", "roms"));

        return new SyntheticInstallation
        {
            FileSystem = fileSystem,
            RootPath = root,
            VersionInfo = versions,
            Environment = new FakeEnvironmentPaths()
        };
    }

    /// <summary>
    /// A portable install: one BGFX executable with everything beside it, including its own
    /// VPinballX.ini rather than one under %AppData%.
    /// </summary>
    public static SyntheticInstallation Portable(string root = @"D:\Portable\VPX", string version = "10.8.1.0")
    {
        var fileSystem = new MockFileSystem();
        var versions = new FakeFileVersionInfoReader();

        fileSystem.AddDirectory(root);

        string exePath = fileSystem.Path.Combine(root, "VPinballX_BGFX.exe");
        fileSystem.AddFile(exePath, new MockFileData(SyntheticPortableExecutable.X64()));
        versions.SetVisualPinball(exePath, version);

        fileSystem.AddFile(fileSystem.Path.Combine(root, "bgfx.dll"), new MockFileData([1, 2, 3]));

        string tablesPath = fileSystem.Path.Combine(root, "MyTables");
        fileSystem.AddDirectory(tablesPath);

        // Portable mode: the settings file sits beside the executable and describes this install.
        fileSystem.AddFile(
            fileSystem.Path.Combine(root, "VPinballX.ini"),
            new MockFileData($"[Player]{System.Environment.NewLine}TablesDirectory = {tablesPath}{System.Environment.NewLine}"));

        return new SyntheticInstallation
        {
            FileSystem = fileSystem,
            RootPath = root,
            VersionInfo = versions,
            Environment = new FakeEnvironmentPaths()
        };
    }

    /// <summary>
    /// The bare minimum that should still be recognised: a single Visual Pinball executable and no
    /// Tables folder. A fresh install looks like this.
    /// </summary>
    public static SyntheticInstallation Minimal(string root = @"D:\Games\VPX")
    {
        var fileSystem = new MockFileSystem();
        var versions = new FakeFileVersionInfoReader();

        fileSystem.AddDirectory(root);
        AddExecutable(fileSystem, versions, root, "VPinballX_GL64.exe", ProcessorArchitecture.X64);

        return new SyntheticInstallation
        {
            FileSystem = fileSystem,
            RootPath = root,
            VersionInfo = versions,
            Environment = new FakeEnvironmentPaths()
        };
    }

    /// <summary>
    /// A folder full of executables that have nothing to do with Visual Pinball. Discovery must
    /// reject this rather than latching onto whatever it finds.
    /// </summary>
    public static SyntheticInstallation Ambiguous(string root = @"D:\Games\SomethingElse")
    {
        var fileSystem = new MockFileSystem();
        var versions = new FakeFileVersionInfoReader();

        fileSystem.AddDirectory(root);

        foreach (string name in (string[])["launcher.exe", "game.exe", "pinball.exe", "setup.exe"])
        {
            string path = fileSystem.Path.Combine(root, name);
            fileSystem.AddFile(path, new MockFileData(SyntheticPortableExecutable.X64()));
        }

        // A tables folder alone must not be enough to call something a Visual Pinball install.
        fileSystem.AddDirectory(fileSystem.Path.Combine(root, "Tables"));

        return new SyntheticInstallation
        {
            FileSystem = fileSystem,
            RootPath = root,
            VersionInfo = versions,
            Environment = new FakeEnvironmentPaths()
        };
    }

    /// <summary>A folder that exists but contains no executables at all.</summary>
    public static SyntheticInstallation Empty(string root = @"D:\Empty")
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(root);
        fileSystem.AddFile(fileSystem.Path.Combine(root, "readme.txt"), new MockFileData("nothing here"));

        return new SyntheticInstallation
        {
            FileSystem = fileSystem,
            RootPath = root,
            VersionInfo = new FakeFileVersionInfoReader(),
            Environment = new FakeEnvironmentPaths()
        };
    }

    private static void AddExecutable(
        MockFileSystem fileSystem,
        FakeFileVersionInfoReader versions,
        string root,
        string fileName,
        ProcessorArchitecture architecture,
        string version = "10.8.0.2058")
    {
        string path = fileSystem.Path.Combine(root, fileName);
        fileSystem.AddFile(path, new MockFileData(SyntheticPortableExecutable.Build(architecture)));
        versions.SetVisualPinball(path, version);
    }
}
