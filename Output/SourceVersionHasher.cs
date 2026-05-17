using System.Security.Cryptography;
using System.Text;

namespace derbyhubDb.Output;

public static class SourceVersionHasher
{
    public static string Compute(string masterMdb, string storyDataDir, string? correctionsDir)
    {
        using var sha = SHA256.Create();
        AppendFile(sha, masterMdb);

        var characterDir = Path.Combine(storyDataDir, "50");
        if (Directory.Exists(characterDir))
        {
            foreach (var file in Directory.EnumerateFiles(characterDir, "storytimeline_*.json", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                AppendText(sha, Path.GetRelativePath(storyDataDir, file).Replace('\\', '/'));
                AppendFile(sha, file);
            }
        }

        if (!string.IsNullOrWhiteSpace(correctionsDir) && Directory.Exists(correctionsDir))
        {
            AppendOptionalFile(sha, Path.Combine(correctionsDir, "correctedEventNames.txt"));
            AppendOptionalFile(sha, Path.Combine(correctionsDir, "correctedTriggerNames.txt"));
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash ?? []).ToLowerInvariant();
    }

    private static void AppendOptionalFile(HashAlgorithm hash, string path)
    {
        if (File.Exists(path))
        {
            AppendFile(hash, path);
        }
    }

    private static void AppendFile(HashAlgorithm hash, string path)
    {
        AppendText(hash, $"file:{Path.GetFileName(path)}:{new FileInfo(path).Length}");
        using var stream = File.OpenRead(path);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.TransformBlock(buffer, 0, read, null, 0);
        }

        AppendText(hash, "\n");
    }

    private static void AppendText(HashAlgorithm hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }
}
