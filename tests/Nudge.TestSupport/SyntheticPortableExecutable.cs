using Nudge.Core.Models;

namespace Nudge.TestSupport;

/// <summary>
/// Builds byte-for-byte valid Windows PE images for tests.
///
/// This exists so that architecture detection is tested against a <em>real</em> PE header parsed by
/// the real <c>PEReader</c>, rather than against a mock that simply returns whatever the test asked
/// for. That distinction matters: the whole point of reading the PE header is that filenames lie, and
/// a test built on a stubbed reader would not actually prove Nudge reads the header at all.
///
/// The images are headers-only. They are structurally valid and parseable, but they contain no code
/// and will not run. Nothing in Nudge ever executes them.
/// </summary>
public static class SyntheticPortableExecutable
{
    private const ushort DosSignature = 0x5A4D;          // "MZ"
    private const uint PeSignature = 0x00004550;         // "PE\0\0"
    private const int PeHeaderOffset = 0x80;

    private const ushort MachineI386 = 0x014C;
    private const ushort MachineAmd64 = 0x8664;

    private const ushort Pe32Magic = 0x010B;
    private const ushort Pe32PlusMagic = 0x020B;

    private const ushort Pe32OptionalHeaderSize = 224;
    private const ushort Pe32PlusOptionalHeaderSize = 240;

    private const int NumberOfDataDirectories = 16;
    private const uint FileAlignment = 0x200;
    private const uint SectionAlignment = 0x1000;

    /// <summary>A 64-bit executable image.</summary>
    public static byte[] X64() => Build(ProcessorArchitecture.X64);

    /// <summary>A 32-bit executable image.</summary>
    public static byte[] X86() => Build(ProcessorArchitecture.X86);

    /// <summary>
    /// An image whose COFF machine field is a value Nudge does not classify, used to prove that an
    /// unrecognised architecture is reported as Unknown rather than defaulted to something.
    /// </summary>
    public static byte[] UnrecognisedArchitecture() => Build(ProcessorArchitecture.Unknown);

    /// <summary>Bytes that are not a PE image at all.</summary>
    public static byte[] NotAnExecutable() =>
        "This is not a Windows executable."u8.ToArray();

    public static byte[] Build(ProcessorArchitecture architecture)
    {
        bool is64Bit = architecture == ProcessorArchitecture.X64;

        ushort machine = architecture switch
        {
            ProcessorArchitecture.X86 => MachineI386,
            ProcessorArchitecture.X64 => MachineAmd64,
            // 0x01C0 is ARM. Nudge does not classify it, which is exactly what we want to test.
            _ => 0x01C0
        };

        ushort optionalHeaderSize = is64Bit ? Pe32PlusOptionalHeaderSize : Pe32OptionalHeaderSize;

        const int sectionCount = 1;
        int headerEnd = PeHeaderOffset + 4 + 20 + optionalHeaderSize + (sectionCount * 40);
        uint sizeOfHeaders = AlignUp((uint)headerEnd, FileAlignment);

        uint sectionRawOffset = sizeOfHeaders;
        const uint sectionRawSize = FileAlignment;
        uint totalFileSize = sectionRawOffset + sectionRawSize;

        var buffer = new byte[totalFileSize];
        var writer = new SpanWriter(buffer);

        // --- DOS header -------------------------------------------------------------------------
        writer.WriteUInt16(DosSignature);
        writer.Seek(0x3C);
        writer.WriteUInt32(PeHeaderOffset);

        // --- PE signature -----------------------------------------------------------------------
        writer.Seek(PeHeaderOffset);
        writer.WriteUInt32(PeSignature);

        // --- COFF header ------------------------------------------------------------------------
        writer.WriteUInt16(machine);
        writer.WriteUInt16(sectionCount);
        writer.WriteUInt32(0);                      // TimeDateStamp
        writer.WriteUInt32(0);                      // PointerToSymbolTable
        writer.WriteUInt32(0);                      // NumberOfSymbols
        writer.WriteUInt16(optionalHeaderSize);

        // IMAGE_FILE_EXECUTABLE_IMAGE, plus the bit-width flag matching the machine type.
        writer.WriteUInt16((ushort)(is64Bit ? 0x0022 : 0x0102));

        // --- Optional header --------------------------------------------------------------------
        writer.WriteUInt16(is64Bit ? Pe32PlusMagic : Pe32Magic);
        writer.WriteByte(14);                       // MajorLinkerVersion
        writer.WriteByte(0);                        // MinorLinkerVersion
        writer.WriteUInt32(sectionRawSize);         // SizeOfCode
        writer.WriteUInt32(0);                      // SizeOfInitializedData
        writer.WriteUInt32(0);                      // SizeOfUninitializedData
        writer.WriteUInt32(SectionAlignment);       // AddressOfEntryPoint
        writer.WriteUInt32(SectionAlignment);       // BaseOfCode

        if (is64Bit)
        {
            writer.WriteUInt64(0x0000000140000000);  // ImageBase
        }
        else
        {
            writer.WriteUInt32(SectionAlignment * 2); // BaseOfData (PE32 only)
            writer.WriteUInt32(0x00400000);           // ImageBase
        }

        writer.WriteUInt32(SectionAlignment);
        writer.WriteUInt32(FileAlignment);
        writer.WriteUInt16(6);                      // MajorOperatingSystemVersion
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);                      // MajorImageVersion
        writer.WriteUInt16(0);
        writer.WriteUInt16(6);                      // MajorSubsystemVersion
        writer.WriteUInt16(0);
        writer.WriteUInt32(0);                      // Win32VersionValue
        writer.WriteUInt32(SectionAlignment * 2);   // SizeOfImage
        writer.WriteUInt32(sizeOfHeaders);
        writer.WriteUInt32(0);                      // CheckSum
        writer.WriteUInt16(2);                      // Subsystem: WINDOWS_GUI
        writer.WriteUInt16(0);                      // DllCharacteristics

        if (is64Bit)
        {
            writer.WriteUInt64(0x100000);           // SizeOfStackReserve
            writer.WriteUInt64(0x1000);             // SizeOfStackCommit
            writer.WriteUInt64(0x100000);           // SizeOfHeapReserve
            writer.WriteUInt64(0x1000);             // SizeOfHeapCommit
        }
        else
        {
            writer.WriteUInt32(0x100000);
            writer.WriteUInt32(0x1000);
            writer.WriteUInt32(0x100000);
            writer.WriteUInt32(0x1000);
        }

        writer.WriteUInt32(0);                      // LoaderFlags
        writer.WriteUInt32(NumberOfDataDirectories);

        for (int i = 0; i < NumberOfDataDirectories; i++)
        {
            writer.WriteUInt32(0);                  // RVA
            writer.WriteUInt32(0);                  // Size
        }

        // --- Section header ---------------------------------------------------------------------
        writer.WriteFixedAscii(".text", 8);
        writer.WriteUInt32(0x10);                   // VirtualSize
        writer.WriteUInt32(SectionAlignment);       // VirtualAddress
        writer.WriteUInt32(sectionRawSize);
        writer.WriteUInt32(sectionRawOffset);
        writer.WriteUInt32(0);                      // PointerToRelocations
        writer.WriteUInt32(0);                      // PointerToLinenumbers
        writer.WriteUInt16(0);                      // NumberOfRelocations
        writer.WriteUInt16(0);                      // NumberOfLinenumbers
        writer.WriteUInt32(0x60000020);             // CODE | EXECUTE | READ

        return buffer;
    }

    private static uint AlignUp(uint value, uint alignment) =>
        (value + alignment - 1) / alignment * alignment;

    /// <summary>Minimal little-endian writer over a byte array, with an explicit cursor.</summary>
    private ref struct SpanWriter(Span<byte> buffer)
    {
        private readonly Span<byte> _buffer = buffer;
        private int _position;

        public void Seek(int position) => _position = position;

        public void WriteByte(byte value) => _buffer[_position++] = value;

        public void WriteUInt16(ushort value)
        {
            _buffer[_position++] = (byte)(value & 0xFF);
            _buffer[_position++] = (byte)(value >> 8);
        }

        public void WriteUInt32(uint value)
        {
            _buffer[_position++] = (byte)(value & 0xFF);
            _buffer[_position++] = (byte)((value >> 8) & 0xFF);
            _buffer[_position++] = (byte)((value >> 16) & 0xFF);
            _buffer[_position++] = (byte)((value >> 24) & 0xFF);
        }

        public void WriteUInt64(ulong value)
        {
            WriteUInt32((uint)(value & 0xFFFFFFFF));
            WriteUInt32((uint)(value >> 32));
        }

        public void WriteFixedAscii(string text, int length)
        {
            for (int i = 0; i < length; i++)
            {
                _buffer[_position++] = i < text.Length ? (byte)text[i] : (byte)0;
            }
        }
    }
}
