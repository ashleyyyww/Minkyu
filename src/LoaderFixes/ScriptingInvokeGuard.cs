using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CustomAlbums.LoaderFixes;

public static class ScriptingInvokeGuard
{
    private const string ExpectedSha256 = "50268324a505edc712d8f43fdfbb3322f8a11d93e325809c4f0df90307222004";
    private static readonly byte[] Signature = Convert.FromHexString("ff4301d1f80b00f9f75b02a9f55303a9f37b04a9f50300aae00302aaf803042af30303aaf60302aaf40301aa78230094");
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint InvokeDelegate(nint method, nint obj, nint args, nint exc, [MarshalAs(UnmanagedType.U1)] bool flag);
    private static readonly InvokeDelegate Detour = Invoke;
    private static InvokeDelegate? original;
    private static bool installed;
    private static int nullCalls;
    [DllImport("libdobby.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern int DobbyHook(nint address, nint replacement, out nint trampoline);
    [DllImport("liblog.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern int __android_log_write(int priority, string tag, string message);
    public static void Install()
    {
        if (installed)
            return;
        if (IntPtr.Size != 8 || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            throw new PlatformNotSupportedException("Scripting invoke guard requires Android ARM64");
        var mappings = File.ReadAllLines("/proc/self/maps").Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length >= 6 && parts[1].Contains('x') && parts[^1].EndsWith("/libunity.so", StringComparison.Ordinal)).ToList();
        if (mappings.Count == 0)
            throw new InvalidOperationException("Executable libunity mapping missing");
        var path = mappings[0][^1];
        var data = File.ReadAllBytes(path);
        if (!Convert.ToHexString(SHA256.HashData(data)).Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Scripting guard unsupported libunity SHA256");
        var(virtualAddress, fileOffset) = FindSignature(data);
        var phoff = checked((int)BitConverter.ToUInt64(data, 32));
        var phsize = BitConverter.ToUInt16(data, 54);
        var phcount = BitConverter.ToUInt16(data, 56);
        nint address = 0;
        foreach (var parts in mappings)
        {
            var mapStart = long.Parse(parts[0].Split('-')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var mapOffset = long.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            for (var index = 0; index < phcount; index++)
            {
                var header = phoff + index * phsize;
                if (BitConverter.ToUInt32(data, header) != 1 || (BitConverter.ToUInt32(data, header + 4) & 1) == 0)
                    continue;
                var segmentOffset = checked((long)BitConverter.ToUInt64(data, header + 8));
                var segmentAddress = checked((long)BitConverter.ToUInt64(data, header + 16));
                var segmentSize = checked((long)BitConverter.ToUInt64(data, header + 32));
                if (fileOffset < segmentOffset || fileOffset >= segmentOffset + segmentSize)
                    continue;
                if (mapOffset < (segmentOffset & ~4095L) || mapOffset >= segmentOffset + segmentSize)
                    continue;
                var bias = mapStart - (segmentAddress + mapOffset - segmentOffset);
                address = new IntPtr(bias + virtualAddress);
                break;
            }

            if (address != 0)
                break;
        }

        if (address == 0)
            throw new InvalidOperationException("Cannot establish scripting invoke load bias");
        var live = new byte[Signature.Length];
        Marshal.Copy(address, live, 0, live.Length);
        if (!live.SequenceEqual(Signature))
            throw new InvalidOperationException("Live scripting invoke signature differs");
        var status = DobbyHook(address, Marshal.GetFunctionPointerForDelegate(Detour), out var trampoline);
        if (status != 0 || trampoline == 0)
            throw new InvalidOperationException($"Scripting invoke DobbyHook status={status}");
        original = Marshal.GetDelegateForFunctionPointer<InvokeDelegate>(trampoline);
        installed = true;
        __android_log_write(4, "CA_LOADER_FIX", $"SCRIPTING_GUARD_INSTALLED addr=0x{address:x} file=0x{fileOffset:x}");
    }

    public static (long VirtualAddress, long FileOffset) FindSignature(byte[] data)
    {
        if (data.Length < 64 || data[0] != 0x7f || data[1] != (byte)'E' || data[4] != 2 || data[5] != 1)
            throw new InvalidDataException("Expected ELF64 little-endian library");
        var offset = -1;
        for (var index = 0; index <= data.Length - Signature.Length; index++)
        {
            if (data[index] != Signature[0] || !data.AsSpan(index, Signature.Length).SequenceEqual(Signature))
                continue;
            if (offset >= 0)
                throw new InvalidDataException("Scripting invoke signature is not unique");
            offset = index;
        }

        if (offset < 0)
            throw new InvalidDataException("Scripting invoke signature missing");
        var phoff = checked((int)BitConverter.ToUInt64(data, 32));
        var phsize = BitConverter.ToUInt16(data, 54);
        var phcount = BitConverter.ToUInt16(data, 56);
        for (var index = 0; index < phcount; index++)
        {
            var header = phoff + index * phsize;
            if (BitConverter.ToUInt32(data, header) != 1 || (BitConverter.ToUInt32(data, header + 4) & 1) == 0)
                continue;
            var segmentOffset = checked((long)BitConverter.ToUInt64(data, header + 8));
            var segmentAddress = checked((long)BitConverter.ToUInt64(data, header + 16));
            var segmentSize = checked((long)BitConverter.ToUInt64(data, header + 32));
            if (offset >= segmentOffset && offset + Signature.Length <= segmentOffset + segmentSize)
                return (segmentAddress + offset - segmentOffset, offset);
        }

        throw new InvalidDataException("Scripting invoke signature is outside executable PT_LOAD");
    }

    private static nint Invoke(nint method, nint obj, nint args, nint exc, bool flag)
    {
        if (method == 0)
        {
            if (Interlocked.Increment(ref nullCalls) <= 3)
                __android_log_write(4, "CA_LOADER_FIX", "SCRIPTING_NULL_METHOD_IGNORED");
            return 0;
        }

        return original!(method, obj, args, exc, flag);
    }
}
