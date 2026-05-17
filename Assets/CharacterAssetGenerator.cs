using System.Text.Encodings.Web;
using System.Text.Json;
using derbyhubDb.UmaEvents;

namespace derbyhubDb.Assets;

public sealed class CharacterAssetGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly GameAssetProbe _probe;
    private readonly CharacterImageResolver _imageResolver;

    public CharacterAssetGenerator(GameAssetProbe probe, CharacterImageResolver imageResolver)
    {
        _probe = probe;
        _imageResolver = imageResolver;
    }

    public async Task<CharacterAssetGenerationResult> GenerateAsync(
        UmaEventSnapshotData snapshot,
        CharacterAssetGeneratorOptions options,
        CancellationToken cancellationToken = default)
    {
        var targetRoot = Path.GetFullPath(options.TargetRoot);
        var publicRoot = string.IsNullOrWhiteSpace(options.DerbyhubPublic)
            ? null
            : Path.GetFullPath(options.DerbyhubPublic);
        var writeToPublic = publicRoot is not null && PathsEqual(targetRoot, publicRoot);
        var backupDir = Path.Combine(targetRoot, "backup", DateTime.Now.ToString("yyyyMMddHHmmss"));
        var context = new CharacterAssetWriteContext(
            options.DryRun,
            options.DownloadMissing,
            options.Force,
            writeToPublic && !options.DryRun,
            backupDir);

        var manifest = new CharacterAssetManifest
        {
            GeneratedAt = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
            GameAssetProbe = _probe.Probe(
                options.GameDataRoot,
                options.MasterMdb,
                options.MetaDb,
                options.Sqlite3McDll,
                BuildLocalAssetLookupRequests(snapshot))
        };
        var localCandidatesByCardId = manifest.GameAssetProbe.LocalAssetCandidates
            .GroupBy(x => x.CardId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<LocalAssetCandidate>)x.ToList());
        manifest.Warnings.Add("characters.json 的 name_zh/name_en 第一阶段暂为空字符串；如前端需要多语言显示，需要后续接入翻译来源。");

        var charactersDocument = new DerbyHubCharactersDocument
        {
            Version = manifest.Version
        };

        manifest.Summary.SnapshotCharacterCount = snapshot.Catalog.CharacterCount;
        manifest.Summary.CatalogVariantCount = snapshot.Catalog.VariantCount;

        foreach (var character in snapshot.Catalog.Characters.OrderBy(x => x.CharacterId))
        {
            var outputCharacter = new DerbyHubCharacter
            {
                Id = character.CharacterId,
                NameJa = character.NameJa,
                NameZh = string.Empty,
                NameEn = string.Empty
            };

            var addedCardIds = new HashSet<int>();
            foreach (var variant in character.Variants
                .OrderBy(x => NormalizeDisplayCardId(character.CharacterId, x.VariantId))
                .ThenBy(x => x.VariantId))
            {
                var cardId = NormalizeDisplayCardId(character.CharacterId, variant.VariantId);
                var avatar = $"chara_{cardId}.png";
                var targetPath = Path.Combine(targetRoot, "assets", "chara", avatar);
                var existingPublicPath = publicRoot is null ? null : Path.Combine(publicRoot, "assets", "chara", avatar);
                var entry = new CharacterAssetManifestEntry
                {
                    CharacterId = character.CharacterId,
                    VariantId = variant.VariantId,
                    CardId = cardId,
                    NameJa = character.NameJa,
                    VariantNameJa = variant.VariantNameJa,
                    LocalPath = targetPath
                };

                if (IsBaseVariant(character, variant))
                {
                    entry.Source = "missing";
                    entry.Status = "skipped-base";
                    entry.Error = "base variant 是事件分类占位，不生成头像";
                    manifest.Entries.Add(entry);
                    manifest.Summary.SkippedBaseVariantCount++;
                    continue;
                }

                if (addedCardIds.Add(cardId))
                {
                    outputCharacter.Variants.Add(new DerbyHubCharacterVariant
                    {
                        CardId = cardId,
                        Avatar = avatar
                    });
                }
                manifest.Summary.RequiredImageVariantCount++;

                await _imageResolver.ResolveAsync(
                    new CharacterImageRequest(
                        character.CharacterId,
                        cardId,
                        targetPath,
                        existingPublicPath,
                        localCandidatesByCardId.GetValueOrDefault(cardId) ?? []),
                    entry,
                    context,
                    cancellationToken);
                manifest.Entries.Add(entry);
            }

            if (outputCharacter.Variants.Count > 0)
            {
                charactersDocument.Characters.Add(outputCharacter);
            }
        }

        PopulateSummary(manifest);

        if (!options.DryRun)
        {
            WriteJson(Path.Combine(targetRoot, "data", "characters.json"), charactersDocument, context);
            WriteJson(Path.Combine(targetRoot, "data", "image_manifest.json"), manifest, context);
        }

        return new CharacterAssetGenerationResult
        {
            Manifest = manifest,
            Characters = charactersDocument,
            TargetRoot = targetRoot,
            DryRun = options.DryRun
        };
    }

    private static bool IsBaseVariant(UmaEventCatalogCharacterResponse character, UmaEventVariantSummaryResponse variant)
    {
        return variant.VariantType.Equals("base", StringComparison.OrdinalIgnoreCase)
            || variant.VariantId == character.BaseVariantId
            || variant.VariantId == character.CharacterId * 100;
    }

    private static int NormalizeDisplayCardId(int characterId, int variantId)
    {
        _ = characterId;
        var text = variantId.ToString();
        if (text.Length == 7 && text[0] == '9')
        {
            return int.Parse(text[1..]);
        }

        return variantId;
    }

    private static List<LocalAssetLookupRequest> BuildLocalAssetLookupRequests(UmaEventSnapshotData snapshot)
    {
        return snapshot.Catalog.Characters
            .SelectMany(character => character.Variants
                .Where(variant => !IsBaseVariant(character, variant))
                .Select(variant => new LocalAssetLookupRequest(
                    character.CharacterId,
                    NormalizeDisplayCardId(character.CharacterId, variant.VariantId))))
            .ToList();
    }

    private static void PopulateSummary(CharacterAssetManifest manifest)
    {
        manifest.Summary.ExistingImageCount = manifest.Entries.Count(x => x.Status == "exists");
        manifest.Summary.LocalGeneratedCount = manifest.Entries.Count(x => x.Source == "local-game" && x.Status == "generated");
        manifest.Summary.GameToraDownloadedCount = manifest.Entries.Count(x => x.Source == "gametora" && x.Status == "downloaded");
        manifest.Summary.MissingCount = manifest.Entries.Count(x => x.Status == "missing");
        manifest.Summary.FailedCount = manifest.Entries.Count(x => x.Status == "failed");
        manifest.Summary.NeedsHumanReviewCount = manifest.Entries.Count(x => x.NeedsHumanReview);
    }

    private static void WriteJson<T>(string outputPath, T value, CharacterAssetWriteContext context)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("输出路径没有目录部分");
        Directory.CreateDirectory(directory);
        BackupIfNeeded(fullPath, context);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, value, JsonOptions);
        }

        File.Move(tempPath, fullPath, overwrite: true);
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

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CharacterAssetGeneratorOptions
{
    public string TargetRoot { get; init; } = string.Empty;
    public string? DerbyhubPublic { get; init; }
    public string GameDataRoot { get; init; } = string.Empty;
    public string MasterMdb { get; init; } = string.Empty;
    public string? MetaDb { get; init; }
    public string? Sqlite3McDll { get; init; }
    public bool DownloadMissing { get; init; }
    public bool DryRun { get; init; }
    public bool Force { get; init; }
}
