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
        bool includeGameStg = true,
        string? gameScript = null)
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

                // Each stream is written and disposed inside its own block before the next one is
                // created - OpenMcdf silently drops the contents of every stream in a storage (they
                // read back at 0 bytes) if more than one is left open at once, discovered the hard
                // way when a "using" declaration (no braces) left "Version" open while "GameData"
                // was created and written.
                using (CfbStream version = gameStg.CreateStream("Version"))
                {
                    version.Write([1, 0, 8, 0], 0, 4);
                }

                if (gameScript is not null)
                {
                    // Real byte-for-byte shape of GameStg\GameData: a sequence of BIFF-style tagged
                    // records (4-byte little-endian length covering tag+payload, then the 4-byte
                    // tag, then the payload), ending in an "ENDB" record. The script lives in the
                    // "CODE" record, whose payload is itself a 4-byte length then that many UTF-8
                    // bytes. Verified against four real table files - see docs/RESEARCH-NOTES.md and
                    // Nudge.Vpx.TableFiles.GameDataScriptReader.
                    using CfbStream gameData = gameStg.CreateStream("GameData");
                    byte[] biff = BuildGameDataBiff(gameScript);
                    gameData.Write(biff, 0, biff.Length);
                }
            }

            root.Flush(consolidate: true);
        }

        return stream.ToArray();
    }

    private static byte[] BuildGameDataBiff(string script)
    {
        using var biffStream = new MemoryStream();
        byte[] scriptBytes = Encoding.UTF8.GetBytes(script);

        // A couple of ordinary records first, so the synthetic stream exercises the reader's
        // record-skipping rather than handing it CODE as the very first thing - real tables carry
        // hundreds of these before the script (a real Medieval Madness has 258).
        WriteInt32LittleEndian(biffStream, 8);
        biffStream.Write("LEFT"u8);
        WriteInt32LittleEndian(biffStream, 0);

        WriteInt32LittleEndian(biffStream, 8);
        biffStream.Write("TOPX"u8);
        WriteInt32LittleEndian(biffStream, 0);

        // CODE's record length is 4 - the tag ONLY - with the script's own length following the tag
        // outside that record length. This is not the same framing as the records above, and getting
        // it wrong is invisible against a synthetic file that shares the mistake: an earlier version
        // of this builder wrote "4 + 4 + length" here, matching a reader that expected the same, and
        // both agreed happily while failing to read every real table on disk. See
        // Nudge.Vpx.TableFiles.GameDataScriptReader and docs/RESEARCH-NOTES.md.
        WriteInt32LittleEndian(biffStream, 4);
        biffStream.Write("CODE"u8);
        WriteInt32LittleEndian(biffStream, scriptBytes.Length);
        biffStream.Write(scriptBytes, 0, scriptBytes.Length);

        WriteInt32LittleEndian(biffStream, 4);
        biffStream.Write("ENDB"u8);

        return biffStream.ToArray();
    }

    private static void WriteInt32LittleEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
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
