namespace derbyhubDb.Assets;

public sealed class CharacterImageResolver
{
    private readonly GameToraImageDownloader _gameTora;
    private readonly UnityCharacterImageExtractor _localExtractor;

    public CharacterImageResolver(GameToraImageDownloader gameTora, UnityCharacterImageExtractor localExtractor)
    {
        _gameTora = gameTora;
        _localExtractor = localExtractor;
    }

    public async Task ResolveAsync(
        CharacterImageRequest request,
        CharacterAssetManifestEntry entry,
        CharacterAssetWriteContext context,
        CancellationToken cancellationToken = default)
    {
        var targetExists = File.Exists(request.TargetPath);
        if (targetExists && !context.Force)
        {
            entry.Source = "existing";
            entry.Status = "exists";
            entry.LocalPath = request.TargetPath;
            entry.FileSize = new FileInfo(request.TargetPath).Length;
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.ExistingPublicPath) && File.Exists(request.ExistingPublicPath) && !context.DryRun)
        {
            BackupIfNeeded(request.TargetPath, context);
            Directory.CreateDirectory(Path.GetDirectoryName(request.TargetPath)!);
            File.Copy(request.ExistingPublicPath, request.TargetPath, overwrite: true);
            entry.Source = "existing";
            entry.SourcePath = request.ExistingPublicPath;
            entry.Status = "exists";
            entry.LocalPath = request.TargetPath;
            entry.FileSize = new FileInfo(request.TargetPath).Length;
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.ExistingPublicPath) && File.Exists(request.ExistingPublicPath) && context.DryRun)
        {
            entry.Source = "existing";
            entry.SourcePath = request.ExistingPublicPath;
            entry.Status = "exists";
            entry.LocalPath = request.TargetPath;
            entry.FileSize = new FileInfo(request.ExistingPublicPath).Length;
            return;
        }

        var localResult = TryResolveLocalGame(request, entry, context);
        if (localResult)
        {
            return;
        }

        entry.SourceUrl = GameToraImageDownloader.BuildUrl(request.CharacterId, request.CardId);
        if (!context.DownloadMissing)
        {
            entry.Source = "missing";
            entry.Status = "missing";
            entry.Error = context.DryRun
                ? "assets dry-run：未写入文件，也未下载缺失图片"
                : "未启用 --download-missing";
            entry.NeedsHumanReview = true;
            return;
        }

        if (context.DryRun)
        {
            entry.Source = "missing";
            entry.Status = "missing";
            entry.Error = "assets dry-run：跳过 GameTora 下载";
            entry.NeedsHumanReview = true;
            return;
        }

        BackupIfNeeded(request.TargetPath, context);
        var download = await _gameTora.DownloadAsync(request.CharacterId, request.CardId, request.TargetPath, cancellationToken);
        entry.SourceUrl = download.Url;
        if (download.Success)
        {
            entry.Source = "gametora";
            entry.Status = "downloaded";
            entry.LocalPath = request.TargetPath;
            entry.FileSize = new FileInfo(request.TargetPath).Length;
            return;
        }

        entry.Source = "missing";
        entry.Status = "missing";
        entry.Error = download.Error;
        entry.NeedsHumanReview = true;
    }

    private bool TryResolveLocalGame(CharacterImageRequest request, CharacterAssetManifestEntry entry, CharacterAssetWriteContext context)
    {
        var exactName = $"chara_stand_{request.CharacterId}_{request.CardId}";
        var candidate = request.LocalCandidates
            .Where(x => x.LocalFileExists)
            .Where(x => x.Name.EndsWith(exactName, StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains($"/{exactName}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name.Length)
            .FirstOrDefault();

        if (candidate is null)
        {
            return false;
        }

        if (!context.DryRun)
        {
            BackupIfNeeded(request.TargetPath, context);
        }

        var result = _localExtractor.TryExtract(candidate, request.TargetPath, context.DryRun, context.Force);
        if (!result.Success)
        {
            entry.SourcePath = candidate.LocalPath;
            entry.Error = $"本地 UnityFS 提取失败: {result.Error}";
            return false;
        }

        entry.Source = "local-game";
        entry.SourcePath = result.SourcePath;
        entry.Status = result.AlreadyExists ? "exists" : "generated";
        entry.LocalPath = request.TargetPath;
        entry.FileSize = result.FileSize;
        if (context.DryRun)
        {
            entry.Error = "assets dry-run：本地资源可提取，但未写入 PNG";
        }

        return true;
    }

    private static void BackupIfNeeded(string targetPath, CharacterAssetWriteContext context)
    {
        if (!context.BackupOverwrites || !File.Exists(targetPath))
        {
            return;
        }

        Directory.CreateDirectory(context.BackupDir);
        var backupPath = Path.Combine(context.BackupDir, Path.GetFileName(targetPath));
        if (!File.Exists(backupPath))
        {
            File.Copy(targetPath, backupPath, overwrite: false);
        }
    }
}

public sealed record CharacterImageRequest(
    int CharacterId,
    int CardId,
    string TargetPath,
    string? ExistingPublicPath,
    IReadOnlyList<LocalAssetCandidate> LocalCandidates);

public sealed record CharacterAssetWriteContext(
    bool DryRun,
    bool DownloadMissing,
    bool Force,
    bool BackupOverwrites,
    string BackupDir);
