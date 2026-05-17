using System.Text.Json.Serialization;

namespace derbyhubDb.Assets;

public sealed class CharacterAssetManifest
{
    public string Version { get; set; } = "derbyhubDb-assets-v1";
    public string GeneratedAt { get; set; } = string.Empty;
    public CharacterAssetSummary Summary { get; set; } = new();
    public GameAssetProbeResult GameAssetProbe { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
    public List<CharacterAssetManifestEntry> Entries { get; set; } = [];
}

public sealed class CharacterAssetSummary
{
    public int SnapshotCharacterCount { get; set; }
    public int CatalogVariantCount { get; set; }
    public int SkippedBaseVariantCount { get; set; }
    public int RequiredImageVariantCount { get; set; }
    public int ExistingImageCount { get; set; }
    public int LocalGeneratedCount { get; set; }
    public int GameToraDownloadedCount { get; set; }
    public int MissingCount { get; set; }
    public int FailedCount { get; set; }
    public int NeedsHumanReviewCount { get; set; }
}

public sealed class CharacterAssetManifestEntry
{
    public int CharacterId { get; set; }
    public int VariantId { get; set; }
    public int CardId { get; set; }
    public string NameJa { get; set; } = string.Empty;
    public string VariantNameJa { get; set; } = string.Empty;
    public string Source { get; set; } = "missing";
    public string? SourcePath { get; set; }
    public string? SourceUrl { get; set; }
    public string? LocalPath { get; set; }
    public string Status { get; set; } = "missing";
    public long FileSize { get; set; }
    public string? Error { get; set; }
    public bool NeedsHumanReview { get; set; }
}

public sealed class DerbyHubCharactersDocument
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "derbyhubDb-assets-v1";

    [JsonPropertyName("characters")]
    public List<DerbyHubCharacter> Characters { get; set; } = [];
}

public sealed class DerbyHubCharacter
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name_ja")]
    public string NameJa { get; set; } = string.Empty;

    [JsonPropertyName("name_zh")]
    public string NameZh { get; set; } = string.Empty;

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("variants")]
    public List<DerbyHubCharacterVariant> Variants { get; set; } = [];

    [JsonPropertyName("compatibility_tags")]
    public List<string> CompatibilityTags { get; set; } = [];
}

public sealed class DerbyHubCharacterVariant
{
    [JsonPropertyName("card_id")]
    public int CardId { get; set; }

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;

    [JsonPropertyName("aptitude")]
    public Dictionary<string, object> Aptitude { get; set; } = [];
}

public sealed class CharacterAssetGenerationResult
{
    public CharacterAssetManifest Manifest { get; init; } = new();
    public DerbyHubCharactersDocument Characters { get; init; } = new();
    public string TargetRoot { get; init; } = string.Empty;
    public bool DryRun { get; init; }

    public void Print()
    {
        var summary = Manifest.Summary;
        Console.WriteLine();
        Console.WriteLine("图像资产报告");
        Console.WriteLine($"资产输出目录: {TargetRoot}");
        Console.WriteLine($"assets dry-run: {DryRun}");
        Console.WriteLine($"snapshot/catalog 角色数: {summary.SnapshotCharacterCount}");
        Console.WriteLine($"catalog variants 总数: {summary.CatalogVariantCount}");
        Console.WriteLine($"跳过的 base variant 数: {summary.SkippedBaseVariantCount}");
        Console.WriteLine($"需要图片的 outfit/card variant 数: {summary.RequiredImageVariantCount}");
        Console.WriteLine($"已有图片数: {summary.ExistingImageCount}");
        Console.WriteLine($"本地提取成功数: {summary.LocalGeneratedCount}");
        Console.WriteLine($"fallback 下载成功数: {summary.GameToraDownloadedCount}");
        Console.WriteLine($"缺失数: {summary.MissingCount}");
        Console.WriteLine($"需要人工检查数: {summary.NeedsHumanReviewCount}");

        if (!string.IsNullOrWhiteSpace(Manifest.GameAssetProbe.Conclusion))
        {
            Console.WriteLine($"本地资源探测结论: {Manifest.GameAssetProbe.Conclusion}");
        }

        if (!string.IsNullOrWhiteSpace(Manifest.GameAssetProbe.MetaDbPath))
        {
            Console.WriteLine($"meta-db: {Manifest.GameAssetProbe.MetaDbPath}");
            Console.WriteLine($"meta-db 可读: {Manifest.GameAssetProbe.MetaDbReadable}");
            Console.WriteLine($"meta-db 加密读取: {Manifest.GameAssetProbe.MetaDbEncrypted}");
            if (!string.IsNullOrWhiteSpace(Manifest.GameAssetProbe.Sqlite3McDllPath))
            {
                Console.WriteLine($"sqlite3mc-dll: {Manifest.GameAssetProbe.Sqlite3McDllPath}");
            }
            if (Manifest.GameAssetProbe.MetaDbReadable)
            {
                Console.WriteLine($"meta entries: {Manifest.GameAssetProbe.MetaEntryCount}");
                Console.WriteLine($"本地资源候选命中数: {Manifest.GameAssetProbe.LocalAssetCandidates.Count}");
            }
            else if (!string.IsNullOrWhiteSpace(Manifest.GameAssetProbe.MetaDbError))
            {
                Console.WriteLine($"meta-db 错误: {Manifest.GameAssetProbe.MetaDbError}");
            }
        }

        var candidates = Manifest.GameAssetProbe.LocalAssetCandidates
            .Where(x => x.CardId is 100503 or 109402 or 111801 or 113001 or 114301 or 9100101 or 9101101)
            .Take(20)
            .ToList();
        if (candidates.Count > 0)
        {
            Console.WriteLine("重点卡资源候选:");
            foreach (var candidate in candidates
                .GroupBy(x => x.CardId)
                .SelectMany(x => x.Take(5)))
            {
                Console.WriteLine($"  {candidate.CharacterId}/{candidate.CardId} {candidate.Type}:{candidate.Name} hash={candidate.Hash} exists={candidate.LocalFileExists}");
            }
        }

        var missing = Manifest.Entries
            .Where(x => x.Status is "missing" or "failed")
            .Take(20)
            .ToList();

        Console.WriteLine("前 20 个缺失条目:");
        if (missing.Count == 0)
        {
            Console.WriteLine("  (无)");
            return;
        }

        foreach (var entry in missing)
        {
            Console.WriteLine($"  {entry.CharacterId}/{entry.CardId} {entry.NameJa} {entry.VariantNameJa} - {entry.Error}");
        }
    }
}
