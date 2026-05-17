using System.Text.Encodings.Web;
using System.Text.Json;
using derbyhubDb.Cli;
using derbyhubDb.UmaEvents;

namespace derbyhubDb.Calculator;

public sealed class CalculatorDataGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public CalculatorGenerationResult Generate(
        UmaEventSnapshotData snapshot,
        CalculatorDataGeneratorOptions options)
    {
        var mode = options.SourceMode;
        var legacy = File.Exists(options.LegacyCharactersJson)
            ? new LegacyCharactersProvider().Load(options.LegacyCharactersJson)
            : LegacyCharactersData.Empty();
        if (legacy.IsEmpty && mode == CalculatorSourceMode.LegacyFallback)
        {
            throw new FileNotFoundException("找不到 legacy characters.json", options.LegacyCharactersJson);
        }
        var localAptitude = new AptitudeProvider().Load(options.MasterMdb);
        var sourceCharacters = BuildSourceCharacters(snapshot);
        var characterIds = sourceCharacters.Select(x => x.CharacterId).OrderBy(x => x).ToList();
        var localCompatibility = new SuccessionRelationBuilder().Build(options.MasterMdb, characterIds);

        var report = new CalculatorGenerationReport
        {
            OutputPath = Path.GetFullPath(options.OutputPath),
            DryRun = options.DryRun,
            SourceMode = mode.ToString(),
            LegacyCharactersJson = Path.GetFullPath(options.LegacyCharactersJson),
            LegacyCharactersJsonFound = !legacy.IsEmpty,
            LocalAptitudeError = localAptitude.Error,
            LocalCompatibilityError = localCompatibility.Error
        };

        var document = new CalculatorCharactersDocument
        {
            Version = $"derbyhubDb-calculator-v1-{DateTimeOffset.UtcNow:yyyyMMdd}"
        };

        var legacyCharacterIds = legacy.Document.Characters.Select(x => x.Id).ToHashSet();
        var legacyCardIds = legacy.Document.Characters
            .SelectMany(x => x.Variants.Select(v => v.CardId))
            .ToHashSet();

        foreach (var sourceCharacter in sourceCharacters.OrderBy(x => x.CharacterId))
        {
            var legacyCharacter = legacy.FindCharacter(sourceCharacter.CharacterId);
            var character = new CalculatorCharacter
            {
                Id = sourceCharacter.CharacterId,
                NameJa = sourceCharacter.NameJa,
                NameZh = ResolveName(
                    sourceCharacter.CharacterId,
                    sourceCharacter.NameJa,
                    legacyCharacter?.NameZh,
                    mode,
                    "name_zh",
                    report),
                NameEn = ResolveName(
                    sourceCharacter.CharacterId,
                    sourceCharacter.NameJa,
                    legacyCharacter?.NameEn,
                    mode,
                    "name_en",
                    report),
                CompatibilityTags = legacyCharacter?.CompatibilityTags is { Count: > 0 }
                    ? [.. legacyCharacter.CompatibilityTags]
                    : []
            };

            var firstSameCharacterAptitude = sourceCharacter.Variants
                .Select(v => localAptitude.AptitudeByCardId.GetValueOrDefault(v.CardId))
                .FirstOrDefault(CalculatorAptitudeKeys.IsComplete);

            foreach (var variantSource in sourceCharacter.Variants.OrderBy(x => x.CardId))
            {
                var legacyVariant = legacy.FindVariant(variantSource.CardId);
                var aptitude = ResolveAptitude(
                    sourceCharacter.CharacterId,
                    variantSource.CardId,
                    localAptitude.AptitudeByCardId.GetValueOrDefault(variantSource.CardId),
                    legacyVariant?.Aptitude,
                    firstSameCharacterAptitude,
                    legacy.FindSameCharacterAptitude(sourceCharacter.CharacterId),
                    mode,
                    report);

                character.Variants.Add(new CalculatorCharacterVariant
                {
                    CardId = variantSource.CardId,
                    Avatar = $"chara_{variantSource.CardId}.png",
                    Aptitude = aptitude
                });
            }

            if (character.Variants.Count > 0)
            {
                document.Characters.Add(character);
            }
        }

        document.CompatibilityTable = ResolveCompatibilityTable(mode, localCompatibility, legacy, report);
        document.TagCompatibility = legacy.Document.TagCompatibility is { Count: > 0 }
            ? new Dictionary<string, int>(legacy.Document.TagCompatibility, StringComparer.Ordinal)
            : [];
        document.GradeThresholds = new Dictionary<string, int>
        {
            ["double_circle"] = 51,
            ["circle"] = 21,
            ["triangle"] = 6,
            ["cross"] = 0
        };

        report.CharacterCount = document.Characters.Count;
        report.VariantCount = document.Characters.Sum(x => x.Variants.Count);
        report.CompatibilityTableCount = document.CompatibilityTable.Count;
        report.TagCompatibilityCount = document.TagCompatibility.Count;
        report.NewCharacterCount = document.Characters.Count(x => !legacyCharacterIds.Contains(x.Id));
        report.NewVariantCount = document.Characters
            .SelectMany(x => x.Variants)
            .Count(x => !legacyCardIds.Contains(x.CardId));
        report.MissingAvatarCount = CountMissingAvatars(document, options, report);

        if (report.CompatibilityTableCount == 0)
        {
            throw new InvalidOperationException("calculator characters.json 不能输出空 compatibility_table");
        }

        ValidateDocument(document);

        if (!options.DryRun)
        {
            WriteJson(options.OutputPath, document);
        }

        return new CalculatorGenerationResult
        {
            Document = document,
            Report = report
        };
    }

    private static List<SourceCharacter> BuildSourceCharacters(UmaEventSnapshotData snapshot)
    {
        var result = new List<SourceCharacter>();
        foreach (var character in snapshot.Catalog.Characters)
        {
            var sourceCharacter = new SourceCharacter(character.CharacterId, character.NameJa);
            var addedCardIds = new HashSet<int>();
            foreach (var variant in character.Variants)
            {
                if (IsBaseVariant(character, variant))
                {
                    continue;
                }

                var cardId = ResolveSearchCardId(character, variant);
                if (cardId is null)
                {
                    continue;
                }

                if (addedCardIds.Add(cardId.Value))
                {
                    sourceCharacter.Variants.Add(new SourceVariant(cardId.Value));
                }
            }

            if (sourceCharacter.Variants.Count > 0)
            {
                result.Add(sourceCharacter);
            }
        }

        return result;
    }

    private static bool IsBaseVariant(UmaEventCatalogCharacterResponse character, UmaEventVariantSummaryResponse variant)
    {
        return variant.VariantType.Equals("base", StringComparison.OrdinalIgnoreCase)
            || variant.VariantId == character.BaseVariantId
            || variant.VariantId == character.CharacterId * 100;
    }

    private static int? ResolveSearchCardId(UmaEventCatalogCharacterResponse character, UmaEventVariantSummaryResponse variant)
    {
        return VariantIdentityResolver.ResolveSearchCardId(character.CharacterId, character.BaseVariantId, variant);
    }

    private static string ResolveName(
        int characterId,
        string nameJa,
        string? legacyName,
        CalculatorSourceMode mode,
        string field,
        CalculatorGenerationReport report)
    {
        if (mode != CalculatorSourceMode.Local && !string.IsNullOrWhiteSpace(legacyName))
        {
            if (field == "name_zh")
            {
                report.NameZhLegacyCount++;
            }
            else
            {
                report.NameEnLegacyCount++;
            }

            return legacyName;
        }

        if (field == "name_zh")
        {
            report.NameZhFallbackCount++;
        }
        else
        {
            report.NameEnFallbackCount++;
        }

        report.NeedsHumanReview.Add($"{characterId}: {field} 缺少本地/legacy 数据，已暂用 name_ja");
        return nameJa;
    }

    private static Dictionary<string, int> ResolveAptitude(
        int characterId,
        int cardId,
        IReadOnlyDictionary<string, int>? local,
        IReadOnlyDictionary<string, int>? legacy,
        IReadOnlyDictionary<string, int>? sameCharacterLocal,
        IReadOnlyDictionary<string, int>? sameCharacterLegacy,
        CalculatorSourceMode mode,
        CalculatorGenerationReport report)
    {
        if (mode != CalculatorSourceMode.LegacyFallback && CalculatorAptitudeKeys.IsComplete(local))
        {
            report.AptitudeLocalCount++;
            return CalculatorAptitudeKeys.Clone(local!);
        }

        if (mode != CalculatorSourceMode.Local && CalculatorAptitudeKeys.IsComplete(legacy))
        {
            report.AptitudeLegacyCount++;
            return CalculatorAptitudeKeys.Clone(legacy!);
        }

        if (mode == CalculatorSourceMode.LegacyFallback && CalculatorAptitudeKeys.IsComplete(local))
        {
            report.AptitudeLocalCount++;
            return CalculatorAptitudeKeys.Clone(local!);
        }

        var sameCharacter = CalculatorAptitudeKeys.IsComplete(sameCharacterLocal)
            ? sameCharacterLocal
            : sameCharacterLegacy;
        if (CalculatorAptitudeKeys.IsComplete(sameCharacter))
        {
            report.AptitudeSameCharacterFallbackCount++;
            report.NeedsHumanReview.Add($"{characterId}/{cardId}: aptitude 缺少精确卡数据，已使用同角色已有衣装适性");
            return CalculatorAptitudeKeys.Clone(sameCharacter!);
        }

        report.AptitudeMissingCount++;
        report.NeedsHumanReview.Add($"{characterId}/{cardId}: aptitude 缺失，已输出全 G 保守占位");
        return CalculatorAptitudeKeys.ConservativeDefault();
    }

    private static Dictionary<string, int> ResolveCompatibilityTable(
        CalculatorSourceMode mode,
        LocalCompatibilityData localCompatibility,
        LegacyCharactersData legacy,
        CalculatorGenerationReport report)
    {
        if (mode != CalculatorSourceMode.LegacyFallback && localCompatibility.CompatibilityTable.Count > 0)
        {
            report.CompatibilityTableSource = "local master.mdb succession_relation";
            return new Dictionary<string, int>(localCompatibility.CompatibilityTable, StringComparer.Ordinal);
        }

        if (legacy.Document.CompatibilityTable.Count > 0)
        {
            report.CompatibilityTableSource = "legacy characters.json";
            return new Dictionary<string, int>(legacy.Document.CompatibilityTable, StringComparer.Ordinal);
        }

        if (localCompatibility.CompatibilityTable.Count > 0)
        {
            report.CompatibilityTableSource = "local master.mdb succession_relation";
            return new Dictionary<string, int>(localCompatibility.CompatibilityTable, StringComparer.Ordinal);
        }

        report.CompatibilityTableSource = "missing";
        return [];
    }

    private static int CountMissingAvatars(
        CalculatorCharactersDocument document,
        CalculatorDataGeneratorOptions options,
        CalculatorGenerationReport report)
    {
        var roots = new List<string> { options.TargetRoot };
        if (!string.IsNullOrWhiteSpace(options.DerbyhubPublic))
        {
            roots.Add(options.DerbyhubPublic);
        }

        if (!string.IsNullOrWhiteSpace(options.LegacyPublicRoot))
        {
            roots.Add(options.LegacyPublicRoot);
        }

        var missing = 0;
        foreach (var variant in document.Characters.SelectMany(x => x.Variants))
        {
            var exists = roots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Any(root => File.Exists(Path.Combine(root, "assets", "chara", variant.Avatar)));
            if (!exists)
            {
                missing++;
                report.NeedsHumanReview.Add($"{variant.CardId}: avatar {variant.Avatar} 在输出 assets/chara 与 DerbyHub public 中均未找到");
            }
        }

        return missing;
    }

    private static void ValidateDocument(CalculatorCharactersDocument document)
    {
        var requiredThresholds = new[] { "double_circle", "circle", "triangle", "cross" };
        if (document.Characters.Count == 0)
        {
            throw new InvalidOperationException("calculator characters.json 至少需要 1 个 character");
        }

        foreach (var character in document.Characters)
        {
            if (character.Variants.Count == 0)
            {
                throw new InvalidOperationException($"character {character.Id} 没有 variant");
            }

            foreach (var variant in character.Variants)
            {
                if (!CalculatorAptitudeKeys.IsComplete(variant.Aptitude))
                {
                    throw new InvalidOperationException($"variant {variant.CardId} aptitude 不完整");
                }
            }
        }

        foreach (var threshold in requiredThresholds)
        {
            if (!document.GradeThresholds.ContainsKey(threshold))
            {
                throw new InvalidOperationException($"grade_thresholds 缺少 {threshold}");
            }
        }
    }

    private static void WriteJson<T>(string outputPath, T value)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("输出路径没有目录部分");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, value, JsonOptions);
        }

        File.Move(tempPath, fullPath, overwrite: true);
    }

    private sealed record SourceCharacter(int CharacterId, string NameJa)
    {
        public List<SourceVariant> Variants { get; } = [];
    }

    private sealed record SourceVariant(int CardId);
}

public sealed class CalculatorDataGeneratorOptions
{
    public string MasterMdb { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string TargetRoot { get; init; } = string.Empty;
    public string LegacyCharactersJson { get; init; } = string.Empty;
    public string? DerbyhubPublic { get; init; }
    public string? LegacyPublicRoot { get; init; }
    public CalculatorSourceMode SourceMode { get; init; } = CalculatorSourceMode.Auto;
    public bool DryRun { get; init; }
}

public sealed class CalculatorGenerationResult
{
    public CalculatorCharactersDocument Document { get; init; } = new();
    public CalculatorGenerationReport Report { get; init; } = new();
}

public sealed class CalculatorGenerationReport
{
    public string OutputPath { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public string SourceMode { get; set; } = string.Empty;
    public string LegacyCharactersJson { get; set; } = string.Empty;
    public bool LegacyCharactersJsonFound { get; set; }
    public string CompatibilityTableSource { get; set; } = string.Empty;
    public string? LocalAptitudeError { get; set; }
    public string? LocalCompatibilityError { get; set; }
    public int CharacterCount { get; set; }
    public int VariantCount { get; set; }
    public int CompatibilityTableCount { get; set; }
    public int TagCompatibilityCount { get; set; }
    public int AptitudeLocalCount { get; set; }
    public int AptitudeLegacyCount { get; set; }
    public int AptitudeSameCharacterFallbackCount { get; set; }
    public int AptitudeMissingCount { get; set; }
    public int NameZhLocalCount { get; set; }
    public int NameZhLegacyCount { get; set; }
    public int NameZhFallbackCount { get; set; }
    public int NameEnLocalCount { get; set; }
    public int NameEnLegacyCount { get; set; }
    public int NameEnFallbackCount { get; set; }
    public int NewCharacterCount { get; set; }
    public int NewVariantCount { get; set; }
    public int MissingAvatarCount { get; set; }
    public List<string> NeedsHumanReview { get; } = [];

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("Calculator data 报告");
        Console.WriteLine($"calculator-out: {OutputPath}");
        Console.WriteLine($"calculator dry-run: {DryRun}");
        Console.WriteLine($"calculator-source: {SourceMode}");
        Console.WriteLine($"legacy characters.json: {LegacyCharactersJson}");
        Console.WriteLine($"legacy characters.json 可读: {LegacyCharactersJsonFound}");
        Console.WriteLine($"characters 数: {CharacterCount}");
        Console.WriteLine($"variants 数: {VariantCount}");
        Console.WriteLine($"compatibility_table 条目数: {CompatibilityTableCount}");
        Console.WriteLine($"compatibility_table 来源: {CompatibilityTableSource}");
        Console.WriteLine($"tag_compatibility 条目数: {TagCompatibilityCount}");
        Console.WriteLine($"aptitude 本地生成数: {AptitudeLocalCount}");
        Console.WriteLine($"aptitude legacy 继承数: {AptitudeLegacyCount}");
        Console.WriteLine($"aptitude 同角色兜底数: {AptitudeSameCharacterFallbackCount}");
        Console.WriteLine($"aptitude 缺失数: {AptitudeMissingCount}");
        Console.WriteLine($"name_zh 本地/legacy/fallback 数: {NameZhLocalCount}/{NameZhLegacyCount}/{NameZhFallbackCount}");
        Console.WriteLine($"name_en 本地/legacy/fallback 数: {NameEnLocalCount}/{NameEnLegacyCount}/{NameEnFallbackCount}");
        Console.WriteLine($"新增角色数: {NewCharacterCount}");
        Console.WriteLine($"新增衣装数: {NewVariantCount}");
        Console.WriteLine($"头像缺失数: {MissingAvatarCount}");
        Console.WriteLine($"needsHumanReview 数: {NeedsHumanReview.Count}");

        if (!string.IsNullOrWhiteSpace(LocalAptitudeError))
        {
            Console.WriteLine($"本地 aptitude 读取警告: {LocalAptitudeError}");
        }

        if (!string.IsNullOrWhiteSpace(LocalCompatibilityError))
        {
            Console.WriteLine($"本地相性表读取警告: {LocalCompatibilityError}");
        }

        Console.WriteLine("前 30 条 needsHumanReview:");
        if (NeedsHumanReview.Count == 0)
        {
            Console.WriteLine("  (无)");
            return;
        }

        foreach (var item in NeedsHumanReview.Take(30))
        {
            Console.WriteLine($"  {item}");
        }
    }
}
