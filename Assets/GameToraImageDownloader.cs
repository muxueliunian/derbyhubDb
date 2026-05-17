namespace derbyhubDb.Assets;

public sealed class GameToraImageDownloader
{
    private readonly HttpClient _httpClient;

    public GameToraImageDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static string BuildUrl(int characterId, int cardId)
    {
        return $"https://gametora.com/images/umamusume/characters/thumb/chara_stand_{characterId}_{cardId}.png";
    }

    public async Task<ImageDownloadResult> DownloadAsync(int characterId, int cardId, string targetPath, CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(characterId, cardId);
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ImageDownloadResult(false, url, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var fullPath = Path.GetFullPath(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("图片输出路径没有目录部分"));

        var tempPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(tempPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        if (new FileInfo(tempPath).Length == 0)
        {
            File.Delete(tempPath);
            return new ImageDownloadResult(false, url, "下载文件为空");
        }

        File.Move(tempPath, fullPath, overwrite: true);
        return new ImageDownloadResult(true, url, null);
    }
}

public sealed record ImageDownloadResult(bool Success, string Url, string? Error);
