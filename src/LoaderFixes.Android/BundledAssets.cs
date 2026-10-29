using System.Security.Cryptography;

namespace Minkyu.LoaderFixes;

internal static class BundledAssets
{
    internal static int CopyTo(string root, Func<string, byte[]> readAsset)
    {
        var copied = 0;
        foreach (var relative in new[]
        {
            "Mods/CustomAlbums.Android.dll",
            "Mods/MuseDashUnlockDLC.dll",
            "UserLibs/CustomAlbums.Core.dll",
            "UserLibs/Newtonsoft.Json.dll",
            "UserLibs/NVorbis.dll",
            "UserLibs/NLayer.dll",
            "MelonLoader/Dependencies/Il2CppAssemblyGenerator/UnityDependencies_2019.4.41.zip"
        }
        )
        {
            var bytes = readAsset("copyToBase/" + relative);
            if (bytes.Length == 0)
                throw new InvalidDataException("Empty bundled dependency " + relative);
            var destination = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                using var existing = File.OpenRead(destination);
                if (existing.Length == bytes.Length && SHA256.HashData(existing).SequenceEqual(SHA256.HashData(bytes)))
                    continue;
            }

            var pending = destination + ".minkyu-pending";
            File.WriteAllBytes(pending, bytes);
            File.Move(pending, destination, true);
            copied++;
        }

        // Earlier prepared games copied this mod under its old name. Don't load both copies.
        var previousUnlock = Path.Combine(root, "Mods/Minkyu.LocalUnlock.dll");
        if (File.Exists(previousUnlock))
            File.Delete(previousUnlock);

        return copied;
    }
}
