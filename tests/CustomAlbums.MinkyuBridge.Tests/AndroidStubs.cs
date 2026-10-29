namespace UnityEngine
{
    public class AndroidJavaObject
    {
        public T Call<T>(string method, params object[] args) => throw new PlatformNotSupportedException("Host has no Android JNI.");
        public void Call(string method, params object[] args) => throw new PlatformNotSupportedException("Host has no Android JNI.");
        public T CallStatic<T>(string method, params object[] args) => throw new PlatformNotSupportedException("Host has no Android JNI.");
        public T GetStatic<T>(string name) => throw new PlatformNotSupportedException("Host has no Android JNI.");
        public void Dispose()
        {
        }
    }

    public class AndroidJavaClass : AndroidJavaObject
    {
        public AndroidJavaClass(string name)
        {
        }
    }

    public static class Application
    {
        public static string persistentDataPath
        {
            get; set;
        }
    }
}

namespace Il2CppSystem
{
    public sealed class String
    {
        public static implicit operator String(string value) => new();
    }
}

namespace CustomAlbums.Android
{
    internal static class Mod
    {
        internal static Logger Log { get; } = new();
    }

    internal sealed class Logger
    {
        public void Msg(string message) => Console.WriteLine(message);
        public void Error(string message) => Console.WriteLine(message);
    }
}
