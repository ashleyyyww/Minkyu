using System.Security.Cryptography;

namespace Minkyu.Shared;

internal static class LaunchFileVerification
{
    // AppContext shares this process-only receipt across the early loader and mod contexts.
    // File identity checks avoid hashing the same large OBB twice during one launch.
    internal const string ReceiptKey = "Minkyu.ExpansionVerification.v1";
    internal static bool Verify(string path, string expectedSha, long maximumBytes, out bool reused)
    {
        reused = false;
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || file.Length <= 0 || file.Length > maximumBytes)
            return false;
        var length = file.Length;
        var modified = file.LastWriteTimeUtc.Ticks;
        var sha = expectedSha.ToLowerInvariant();
        if (AppContext.GetData(ReceiptKey) is object[] receipt && receipt.Length == 4 &&
            receipt[0] is string verifiedPath && verifiedPath == file.FullName &&
            receipt[1] is string verifiedSha && verifiedSha == sha &&
            receipt[2] is long verifiedLength && verifiedLength == length &&
            receipt[3] is long verifiedTime && verifiedTime == modified)
        {
            reused = true;
            return true;
        }

        AppContext.SetData(ReceiptKey, null);
        using var stream = File.OpenRead(file.FullName);
        if (stream.Length != length)
            return false;
        var actualSha = Convert.ToHexString(SHA256.HashData(stream));
        file.Refresh();
        if (!actualSha.Equals(sha, StringComparison.OrdinalIgnoreCase) || !file.Exists || file.Length != length || file.LastWriteTimeUtc.Ticks != modified)
            return false;
        AppContext.SetData(ReceiptKey, new object[] { file.FullName, sha, length, modified });
        return true;
    }
}
