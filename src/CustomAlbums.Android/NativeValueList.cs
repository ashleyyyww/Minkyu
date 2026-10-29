using Il2CppGameLogic;
using Il2CppInterop.Runtime;

namespace CustomAlbums.Android;

internal static class NativeValueList
{
    internal static unsafe void AddChecked(Il2CppSystem.Collections.Generic.List<MusicData> list, MusicData value)
    {
        // Generic List<MusicData>.Add corrupts the unboxed struct on this interop build.
        // IList performs the native copy; verify its bytes before gameplay uses it.
        var index = list.System_Collections_IList_Add(value);
        var copy = list[index];
        uint alignment = 0;
        var size = checked((int)IL2CPP.il2cpp_class_value_size(Il2CppClassPointerStore<MusicData>.NativeClassPtr, ref alignment));
        var expected = new ReadOnlySpan<byte>((void*)IL2CPP.il2cpp_object_unbox(value.Pointer), size);
        var actual = new ReadOnlySpan<byte>((void*)IL2CPP.il2cpp_object_unbox(copy.Pointer), size);
        if (!expected.SequenceEqual(actual))
            throw new InvalidDataException($"Native MusicData copy differs at index {index}");
        GC.KeepAlive(value);
        GC.KeepAlive(copy);
    }
}
