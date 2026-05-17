using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace derbyhubDb.Assets;

public sealed class UnityCharacterImageExtractor
{
    public CharacterLocalImageResult TryExtract(
        LocalAssetCandidate candidate,
        string targetPath,
        bool dryRun,
        bool force)
    {
        if (string.IsNullOrWhiteSpace(candidate.LocalPath) || !File.Exists(candidate.LocalPath))
        {
            return CharacterLocalImageResult.Failed("本地候选资源文件不存在");
        }

        try
        {
            var bundleBytes = UmaViewerAssetBundleCrypto.ReadAndDecryptIfNeeded(candidate.LocalPath, candidate.EncryptionKey);
            var files = UnityFsBundleReader.ReadFiles(bundleBytes);
            var textureData = files
                .Where(x => x.FileName.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Data.Length)
                .FirstOrDefault();
            if (textureData is null)
            {
                return CharacterLocalImageResult.Failed("UnityFS bundle 中未找到 .resS 纹理数据");
            }

            if (!TryInferSquareDxt5Size(textureData.Data.Length, out var width, out var height))
            {
                return CharacterLocalImageResult.Failed($".resS 大小无法按 DXT5 正方形头像推断: {textureData.Data.Length}");
            }

            if (dryRun)
            {
                return CharacterLocalImageResult.Generated(candidate.LocalPath, targetPath, textureData.Data.Length);
            }

            if (File.Exists(targetPath) && !force)
            {
                return CharacterLocalImageResult.Exists(candidate.LocalPath, targetPath, new FileInfo(targetPath).Length);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var rgba = Dxt5Decoder.DecodeRgba(textureData.Data, width, height, flipVertical: true);
            using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
            var tempPath = Path.Combine(
                Path.GetDirectoryName(targetPath)!,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
            image.SaveAsPng(tempPath);
            File.Move(tempPath, targetPath, overwrite: true);

            return CharacterLocalImageResult.Generated(candidate.LocalPath, targetPath, new FileInfo(targetPath).Length);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            return CharacterLocalImageResult.Failed(ex.Message);
        }
    }

    private static bool TryInferSquareDxt5Size(int byteCount, out int width, out int height)
    {
        var side = (int)Math.Sqrt(byteCount);
        if (side > 0 && side * side == byteCount && side % 4 == 0)
        {
            width = side;
            height = side;
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }
}

public sealed record CharacterLocalImageResult(
    bool Success,
    bool AlreadyExists,
    string? SourcePath,
    string? LocalPath,
    long FileSize,
    string? Error)
{
    public static CharacterLocalImageResult Generated(string sourcePath, string localPath, long fileSize)
    {
        return new CharacterLocalImageResult(true, false, sourcePath, localPath, fileSize, null);
    }

    public static CharacterLocalImageResult Exists(string sourcePath, string localPath, long fileSize)
    {
        return new CharacterLocalImageResult(true, true, sourcePath, localPath, fileSize, null);
    }

    public static CharacterLocalImageResult Failed(string error)
    {
        return new CharacterLocalImageResult(false, false, null, null, 0, error);
    }
}
