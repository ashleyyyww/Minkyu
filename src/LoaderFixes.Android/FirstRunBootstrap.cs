using MelonLoader;
using MelonLoader.Utils;
using System.Security.Cryptography;
using JNISharp.NativeInterface;
using Microsoft.Win32.SafeHandles;

namespace Minkyu.LoaderFixes;

public static class FirstRunBootstrap
{
    public static void CopyAssets()
    {
        var copied = BundledAssets.CopyTo(MelonEnvironment.MelonBaseDirectory, APKAssetManager.GetAssetBytes);
        MelonLogger.Msg("MK_BOOTSTRAP_ASSETS_READY copied=" + copied);
        // Unity needs the OBB before game scripts run; the regular mod initializes too late.
        ImportExpansion();
    }

    private static void ImportExpansion()
    {
        static JObject Call(JObject obj, string name, string signature, params JValue[] args) => JNI.CallObjectMethod<JObject>(obj, JNI.GetMethodID(JNI.GetObjectClass(obj), name, signature), args);
        static string StringExtra(JObject intent, string name)
        {
            using var key = JNI.NewString(name);
            using var value = JNI.CallObjectMethod<JString>(intent, JNI.GetMethodID(JNI.GetObjectClass(intent), "getStringExtra", "(Ljava/lang/String;)Ljava/lang/String;"), new JValue(key));
            return value == null || !value.Valid() ? "" : JNI.GetJStringString(value);
        }

        using var unity = JNI.FindClass("com/unity3d/player/UnityPlayer");
        using var activity = JNI.GetStaticObjectField<JObject>(unity, JNI.GetStaticFieldID(unity, "currentActivity", "Landroid/app/Activity;"));
        using var intent = Call(activity, "getIntent", "()Landroid/content/Intent;");
        var uri = StringExtra(intent, "minkyu.expansion");
        if (string.IsNullOrEmpty(uri))
            return;
        if (uri != "content://app.minkyu.manager.library/expansion")
            throw new InvalidDataException("Unexpected expansion provider");
        var sha = StringExtra(intent, "minkyu.expansion.sha256");
        if (sha.Length != 64 || sha.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Invalid expansion hash");
        using var directory = Call(activity, "getObbDir", "()Ljava/io/File;");
        using var path = JNI.CallObjectMethod<JString>(directory, JNI.GetMethodID(JNI.GetObjectClass(directory), "getAbsolutePath", "()Ljava/lang/String;"));
        var location = JNI.GetJStringString(path);
        Directory.CreateDirectory(location);
        var destination = Path.Combine(location, "main.626.com.prpr.musedash.obb");
        if (Minkyu.Shared.LaunchFileVerification.Verify(destination, sha, 4L * 1024 * 1024 * 1024, out _))
            return;

        using var resolver = Call(activity, "getContentResolver", "()Landroid/content/ContentResolver;");
        using var uriClass = JNI.FindClass("android/net/Uri");
        using var uriString = JNI.NewString(uri);
        using var parsed = JNI.CallStaticObjectMethod<JObject>(uriClass, JNI.GetStaticMethodID(uriClass, "parse", "(Ljava/lang/String;)Landroid/net/Uri;"), new JValue(uriString));
        using var mode = JNI.NewString("r");
        using var parcel = Call(resolver, "openFileDescriptor", "(Landroid/net/Uri;Ljava/lang/String;)Landroid/os/ParcelFileDescriptor;", new JValue(parsed), new JValue(mode));
        if (JNI.ExceptionCheck())
        {
            var error = JNI.ExceptionOccurred();
            JNI.ExceptionClear();
            throw new IOException(error.GetMessage());
        }

        if (parcel == null || !parcel.Valid())
            throw new IOException("Manager did not grant expansion access");
        var fd = JNI.CallMethod<int>(parcel, JNI.GetMethodID(JNI.GetObjectClass(parcel), "detachFd", "()I"));
        if (fd < 0)
            throw new IOException("Expansion descriptor unavailable");
        var pending = destination + ".minkyu-pending";
        try
        {
            using (var handle = new SafeFileHandle((IntPtr)fd, true))
            using (var source = new FileStream(handle, FileAccess.Read))
            using (var output = File.Create(pending))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                int count;
                while ((count = source.Read(buffer)) > 0)
                {
                    total += count;
                    if (total > 4L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("Expansion exceeds limit");
                    output.Write(buffer, 0, count);
                }

                if (total == 0)
                    throw new InvalidDataException("Empty expansion");
                output.Flush(true);
            }

            using (var imported = File.OpenRead(pending))
                if (!Convert.ToHexString(SHA256.HashData(imported)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Expansion hash mismatch");
            File.Move(pending, destination, true);
            MelonLogger.Msg("MK_BOOTSTRAP_OBB_READY reused=false");
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
            JNI.CallVoidMethod(parcel, JNI.GetMethodID(JNI.GetObjectClass(parcel), "close", "()V"));
        }
    }
}

public static class BootstrapEntrypoint
{
    public static void Install(string payload)
    {
        var assembly = System.Reflection.Assembly.Load(Convert.FromBase64String(payload));
        assembly.GetType("Minkyu.LoaderFixes.FirstRunBootstrap")!.GetMethod("CopyAssets")!.Invoke(null, null);
    }
}
