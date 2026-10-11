using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Runner;

/// <summary>
/// The shared libraries a native program loads at start-up, read from its PE import tables
/// (Windows) or its ELF DT_NEEDED entries (Linux), and which of them count as operating-system
/// libraries: a C or C++ runtime never does.
/// </summary>
static class NativeImports
{
    // Windows: C and C++ runtimes. Some live in System32 (ucrtbase, msvcrt, an installed VC++
    // redistributable), but none is an operating-system API, so each language must link its own.
    static readonly string[] WindowsRuntimes =
        ["api-ms-win-crt-", "ucrtbase", "vcruntime", "msvcp", "msvcr", "concrt", "vccorlib", "libstdc++", "libgcc", "libwinpthread"];

    // Linux: glibc and the GCC support library that glibc itself depends on.
    static readonly HashSet<string> LinuxSystem =
        ["libc.so.6", "libm.so.6", "libpthread.so.0", "libdl.so.2", "librt.so.1", "ld-linux-x86-64.so.2", "libgcc_s.so.1"];

    /// <summary>
    /// Libraries the file imports, or null when it is not a native executable or library. Managed
    /// .NET assemblies count as not native: their nominal mscoree.dll import is never loaded.
    /// </summary>
    public static IReadOnlyList<string>? Read(string path)
    {
        var magic = new byte[4];
        using (var stream = File.OpenRead(path))
            if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) < magic.Length)
                return null;
        if (magic[0] == 'M' && magic[1] == 'Z')
            return ReadPe(File.ReadAllBytes(path));
        if (magic[0] == 0x7F && magic[1] == 'E' && magic[2] == 'L' && magic[3] == 'F')
            return ReadElf(File.ReadAllBytes(path));
        return null;
    }

    public static bool IsSystem(string library)
    {
        string name = library.ToLowerInvariant();
        if (name.EndsWith(".dll"))
            return !WindowsRuntimes.Any(name.StartsWith)
                && (name.StartsWith("api-ms-win-") || name.StartsWith("ext-ms-")
                    || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name)));
        return LinuxSystem.Contains(name);
    }

    static List<string>? ReadPe(byte[] bytes)
    {
        using var pe = new PEReader(ImmutableArray.Create(bytes));
        if (pe.PEHeaders.CorHeader != null)
            return null;
        var headers = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("no PE optional header");
        var names = new List<string>();
        // IMAGE_IMPORT_DESCRIPTOR: 20 bytes, DLL name RVA at offset 12; ends with an all-zero entry.
        if (headers.ImportTableDirectory.RelativeVirtualAddress is int imports and not 0)
            for (int at = imports; ReadInt32(pe, at + 12) is int name and not 0; at += 20)
                names.Add(ReadString(pe, name));
        // ImgDelayDescr: 32 bytes, DLL name RVA at offset 4; ends with an all-zero entry.
        if (headers.DelayImportTableDirectory.RelativeVirtualAddress is int delayed and not 0)
            for (int at = delayed; ReadInt32(pe, at + 4) is int name and not 0; at += 32)
                names.Add(ReadString(pe, name));
        return names;
    }

    static int ReadInt32(PEReader pe, int rva) => pe.GetSectionData(rva).GetReader().ReadInt32();

    static string ReadString(PEReader pe, int rva)
    {
        var reader = pe.GetSectionData(rva).GetReader();
        int length = reader.IndexOf(0);
        return reader.ReadUTF8(length < 0 ? reader.RemainingBytes : length);
    }

    static List<string> ReadElf(byte[] bytes)
    {
        if (bytes[4] != 2 || bytes[5] != 1)
            throw new InvalidDataException("only 64-bit little-endian ELF files are supported");
        var span = bytes.AsSpan();
        long phoff = BinaryPrimitives.ReadInt64LittleEndian(span[0x20..]);
        int phentsize = BinaryPrimitives.ReadUInt16LittleEndian(span[0x36..]);
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(span[0x38..]);

        var loads = new List<(ulong Address, ulong Offset, ulong Size)>();
        (long Offset, long Size)? dynamic = null;
        for (int i = 0; i < phnum; i++)
        {
            var ph = span[(int)(phoff + (long)i * phentsize)..];
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(ph);
            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(ph[8..]);
            ulong address = BinaryPrimitives.ReadUInt64LittleEndian(ph[16..]);
            ulong fileSize = BinaryPrimitives.ReadUInt64LittleEndian(ph[32..]);
            if (type == 1) // PT_LOAD
                loads.Add((address, offset, fileSize));
            else if (type == 2) // PT_DYNAMIC
                dynamic = ((long)offset, (long)fileSize);
        }
        if (dynamic is not var (dynOffset, dynSize))
            return []; // statically linked

        // Dynamic entries are (tag, value) pairs of 8 bytes each: DT_NEEDED = 1, DT_STRTAB = 5, DT_NULL = 0.
        var needed = new List<long>();
        ulong strtab = 0;
        for (long at = dynOffset; at + 16 <= dynOffset + dynSize; at += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(span[(int)at..]);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(span[(int)(at + 8)..]);
            if (tag == 0)
                break;
            if (tag == 1)
                needed.Add((long)value);
            else if (tag == 5)
                strtab = value;
        }

        var load = loads.FirstOrDefault(l => strtab >= l.Address && strtab < l.Address + l.Size);
        if (load.Size == 0)
            throw new InvalidDataException("DT_STRTAB is outside every loaded segment");
        long strings = (long)(load.Offset + (strtab - load.Address));
        var names = new List<string>();
        foreach (long n in needed)
        {
            var s = span[(int)(strings + n)..];
            names.Add(Encoding.UTF8.GetString(s[..s.IndexOf((byte)0)]));
        }
        return names;
    }
}
