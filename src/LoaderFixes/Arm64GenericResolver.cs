using System.Runtime.InteropServices;

namespace CustomAlbums.LoaderFixes;

public static class Arm64GenericResolver
{
    public static nint Resolve2019(nint virtualMethodExport)
    {
        if (IntPtr.Size != 8 || !BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("Expected little-endian ARM64 runtime");
        var instruction = unchecked((uint)Marshal.ReadInt32(virtualMethodExport));
        if ((instruction & 0xfc000000u) != 0x14000000u)
            throw new InvalidOperationException("Expected ARM64 direct B export thunk");
        var displacement = (long)((int)(instruction << 6) >> 4);
        var implementation = virtualMethodExport.ToInt64() + displacement;
        long lastCall = 0;
        var returned = false;
        for (var index = 0; index < 250; index++)
        {
            var address = implementation + index * 4;
            instruction = unchecked((uint)Marshal.ReadInt32(new IntPtr(address)));
            if ((instruction & 0xfffffc1fu) == 0xd65f0000u)
            {
                returned = true;
                break;
            }

            if ((instruction & 0xfc000000u) == 0x94000000u)
            {
                displacement = (long)((int)(instruction << 6) >> 4);
                lastCall = address + displacement;
            }
        }

        if (!returned || lastCall == 0 || (lastCall & 3) != 0)
            throw new InvalidOperationException("ARM64 generic method target not found within bounded function");
        return new IntPtr(lastCall);
    }
}
