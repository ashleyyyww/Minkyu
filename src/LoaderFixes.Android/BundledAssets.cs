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
            "Mods/Minkyu.Mods.Android.dll",
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

        foreach (var name in new[] { "MuseDashUnlockDLC.dll", "Minkyu.LocalUnlock.dll" })
        {
            var obsolete = Path.Combine(root, "Mods", name);
            if (File.Exists(obsolete))
                File.Delete(obsolete);
        }

        return copied;
    }
}
