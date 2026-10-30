using Il2CppInterop.Runtime;

namespace CustomAlbums.Utilities;

public class Interop
{
    public static T CreateTypeValue<T>()
    {
        return (T)Activator.CreateInstance(typeof(T), IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<T>.NativeClassPtr));
    }
}
