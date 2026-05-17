using System.Text.Json;
using System.Text.Encodings.Web;
using derbyhubDb.UmaEvents;

namespace derbyhubDb.Output;

public static class SnapshotWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write(string outputPath, UmaEventSnapshotData snapshot)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("输出路径没有目录部分");
        Directory.CreateDirectory(directory);

        if (File.Exists(fullPath))
        {
            var backupDir = Path.Combine(directory, "backup");
            Directory.CreateDirectory(backupDir);
            var backupName = $"{Path.GetFileNameWithoutExtension(fullPath)}.{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(fullPath)}";
            File.Copy(fullPath, Path.Combine(backupDir, backupName), overwrite: false);
        }

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, snapshot, JsonOptions);
        }

        File.Move(tempPath, fullPath, overwrite: true);
        Console.WriteLine($"snapshot written: {fullPath}");
    }
}
