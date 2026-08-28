using System.Text;
using OpenMcdf;

namespace Nudge.TestSupport;

/// <summary>
/// Builds byte-for-byte real OLE compound documents shaped like a <c>.vpx</c> table file, using the
/// same OpenMcdf library Nudge ships with. Mirrors <see cref="SyntheticPortableExecutable"/>'s
/// reasoning from Phase 1: a test built against a real, parseable file proves the reader actually
/// works against the real file format, not against a shape a mock was told to hand back.
/// </summary>
public static class SyntheticVpxFile
{
    /// <summary>
    /// Builds a minimal but real .vpx-shaped OLE file with a <c>TableInfo</c> storage containing
    /// the given fields, written exactly as real Visual Pinball writes them: plain UTF-16LE text,
    /// no length prefix, no terminator. Any field left null is omitted entirely, matching how a
    /// real table with a blank field has no stream for it at all.
    /// </summary>
    public static byte[] Build(
        string? tableName = null,
        string? authorName = null,
        string? authorEmail = null,
        string? authorWebSite = null,
        string? releaseDate = null,
        string? tableVersion = null,
        string? tableBlurb = null,
        string? tableDescription = null,
        string? tableRules = null,
        bool includeGameStg = true)
    {
        var stream = new MemoryStream();

        using (RootStorage root = RootStorage.Create(stream, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            Storage tableInfo = root.CreateStorage("TableInfo");
            WriteIfNotNull(tableInfo, "TableName", tableName);
            WriteIfNotNull(tableInfo, "AuthorName", authorName);
            WriteIfNotNull(tableInfo, "AuthorEmail", authorEmail);
            WriteIfNotNull(tableInfo, "AuthorWebSite", authorWebSite);
            WriteIfNotNull(tableInfo, "ReleaseDate", releaseDate);
            WriteIfNotNull(tableInfo, "TableVersion", tableVersion);
            WriteIfNotNull(tableInfo, "TableBlurb", tableBlurb);
            WriteIfNotNull(tableInfo, "TableDescription", tableDescription);
            WriteIfNotNull(tableInfo, "TableRules", tableRules);

            if (includeGameStg)
            {
                // Real tables keep almost all their bulk here (images, sound, the script). Phase 2
                // never reads this storage, but it is included so the synthetic file's shape matches
                // a real one - a reader that accidentally assumed TableInfo was the only storage
                // would still pass against a file that only had TableInfo.
                Storage gameStg = root.CreateStorage("GameStg");
                using CfbStream version = gameStg.CreateStream("Version");
                version.Write([1, 0, 8, 0], 0, 4);
            }

            root.Flush(consolidate: true);
        }

        return stream.ToArray();
    }

    /// <summary>A file with no TableInfo storage at all - a valid OLE file, but not a VPX table.</summary>
    public static byte[] BuildWithoutTableInfo()
    {
        var stream = new MemoryStream();
        using (RootStorage root = RootStorage.Create(stream, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            Storage other = root.CreateStorage("SomeOtherStorage");
            using CfbStream s = other.CreateStream("Data");
            s.Write([1, 2, 3], 0, 3);
            root.Flush(consolidate: true);
        }

        return stream.ToArray();
    }

    /// <summary>Bytes that are not an OLE compound document at all.</summary>
    public static byte[] NotAnOleFile() => "This is a plain text file, not a .vpx table."u8.ToArray();

    private static void WriteIfNotNull(Storage storage, string streamName, string? value)
    {
        if (value is null)
        {
            return;
        }

        byte[] bytes = Encoding.Unicode.GetBytes(value);
        using CfbStream cfbStream = storage.CreateStream(streamName);
        cfbStream.Write(bytes, 0, bytes.Length);
    }
}
