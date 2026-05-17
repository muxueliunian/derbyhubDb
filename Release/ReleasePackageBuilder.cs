using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using derbyhubDb.Assets;
using derbyhubDb.Calculator;
using derbyhubDb.UmaEvents;

namespace derbyhubDb.Release;

public sealed class ReleasePackageBuilder
{
    private const string GeneratorVersion = "derbyhubDb-release-package-v1";
    private const string ReleaseSourceType = "derbyhub-release";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] PackageFileNames =
    [
        "derbyhub-data-manifest.json",
        "snapshot.json.br",
        "characters.json.br",
        "image_manifest.json.br",
        "chara-assets.zip",
        "generation-report.json",
        "needs-human-review.json",
        "sha256sums.txt"
    ];

    public ReleasePackageResult Build(
        UmaEventSnapshotData snapshot,
        ReleasePackageBuilderOptions options,
        CalculatorGenerationResult? calculatorResult = null,
        CharacterAssetGenerationResult? assetResult = null)
    {
        var outputDir = Path.GetFullPath(options.OutputDirectory);
        var releaseTag = RequireValue(options.ReleaseTag, "--release-tag");
        var generatedAt = DateTimeOffset.UtcNow.ToString("O");
        var calculatorPath = ResolveCalculatorCharactersPath(options.CalculatorOutputPath);
        var calculatorRoot = ResolveCalculatorRoot(calculatorPath);
        var assetsRoot = ResolveAssetsRoot(options.AssetsRoot, calculatorRoot);
        var imageManifestPath = ResolveImageManifestPath(options.ImageManifestPath, assetsRoot, calculatorRoot);
        var charaAssetsDir = Path.Combine(assetsRoot, "assets", "chara");

        if (!File.Exists(calculatorPath) && calculatorResult is null)
        {
            throw new FileNotFoundException("找不到 calculator characters.json", calculatorPath);
        }

        if (!File.Exists(imageManifestPath) && assetResult is null)
        {
            throw new FileNotFoundException("找不到 image_manifest.json", imageManifestPath);
        }

        if (!Directory.Exists(charaAssetsDir))
        {
            throw new DirectoryNotFoundException($"找不到 assets/chara 目录: {charaAssetsDir}");
        }

        var releaseSnapshot = Clone(snapshot);
        var originalSourceVersion = releaseSnapshot.Manifest.SourceVersion;
        ApplyReleaseSource(releaseSnapshot, releaseTag);

        var characters = calculatorResult is not null
            ? Clone(calculatorResult.Document)
            : ReadJson<CalculatorCharactersDocument>(calculatorPath);
        characters.Version = releaseTag;

        var imageManifestBytes = assetResult is not null
            ? SerializeToBytes(assetResult.Manifest)
            : File.ReadAllBytes(imageManifestPath);
        var imageManifest = TryReadImageManifest(imageManifestBytes);
        var pngFiles = Directory.GetFiles(charaAssetsDir, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        var review = BuildReviewDocument(calculatorResult?.Report, imageManifest, generatedAt);

        if (options.DryRun)
        {
            return new ReleasePackageResult
            {
                OutputDirectory = outputDir,
                DryRun = true,
                Manifest = BuildReleaseManifest(
                    releaseSnapshot,
                    characters,
                    releaseTag,
                    options.Channel,
                    generatedAt,
                    originalSourceVersion,
                    pngFiles.Count,
                    []),
                GenerationReport = BuildGenerationReport(
                    outputDir,
                    releaseTag,
                    options.Channel,
                    generatedAt,
                    originalSourceVersion,
                    calculatorPath,
                    imageManifestPath,
                    assetsRoot,
                    pngFiles.Count,
                    review.Items.Count,
                    imageManifest)
            };
        }

        Directory.CreateDirectory(outputDir);
        DeleteKnownPackageFiles(outputDir);

        WriteBrotliJson(Path.Combine(outputDir, "snapshot.json.br"), releaseSnapshot);
        WriteBrotliJson(Path.Combine(outputDir, "characters.json.br"), characters);
        WriteBrotliBytes(Path.Combine(outputDir, "image_manifest.json.br"), imageManifestBytes);
        WriteCharaAssetsZip(Path.Combine(outputDir, "chara-assets.zip"), pngFiles);

        var generationReport = BuildGenerationReport(
            outputDir,
            releaseTag,
            options.Channel,
            generatedAt,
            originalSourceVersion,
            calculatorPath,
            imageManifestPath,
            assetsRoot,
            pngFiles.Count,
            review.Items.Count,
            imageManifest);
        WriteJson(Path.Combine(outputDir, "generation-report.json"), generationReport);
        WriteJson(Path.Combine(outputDir, "needs-human-review.json"), review);

        var payloadAssets = BuildPackageAssets(outputDir,
        [
            "snapshot.json.br",
            "characters.json.br",
            "image_manifest.json.br",
            "chara-assets.zip",
            "generation-report.json",
            "needs-human-review.json"
        ]);

        var releaseManifest = BuildReleaseManifest(
            releaseSnapshot,
            characters,
            releaseTag,
            options.Channel,
            generatedAt,
            originalSourceVersion,
            pngFiles.Count,
            payloadAssets);
        WriteJson(Path.Combine(outputDir, "derbyhub-data-manifest.json"), releaseManifest);

        var sums = BuildPackageAssets(outputDir, PackageFileNames.Where(x => x != "sha256sums.txt"));
        WriteSha256Sums(Path.Combine(outputDir, "sha256sums.txt"), sums);

        return new ReleasePackageResult
        {
            OutputDirectory = outputDir,
            DryRun = false,
            Manifest = releaseManifest,
            GenerationReport = generationReport
        };
    }

    private static string ResolveCalculatorCharactersPath(string calculatorOutputPath)
    {
        var fullPath = Path.GetFullPath(RequireValue(calculatorOutputPath, "--calculator-out"));
        return Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.Combine(fullPath, "data", "characters.json");
    }

    private static string ResolveCalculatorRoot(string calculatorCharactersPath)
    {
        var dataDir = Path.GetDirectoryName(calculatorCharactersPath)
            ?? throw new InvalidOperationException("calculator characters.json 路径无效");
        return Path.GetFileName(dataDir).Equals("data", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(dataDir) ?? dataDir
            : dataDir;
    }

    private static string ResolveAssetsRoot(string? assetsRoot, string calculatorRoot)
    {
        return string.IsNullOrWhiteSpace(assetsRoot)
            ? calculatorRoot
            : Path.GetFullPath(assetsRoot);
    }

    private static string ResolveImageManifestPath(string? explicitPath, string assetsRoot, string calculatorRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var candidates = new[]
        {
            Path.Combine(assetsRoot, "data", "image_manifest.json"),
            Path.Combine(calculatorRoot, "data", "image_manifest.json"),
            Path.Combine(Environment.CurrentDirectory, "data", "image_manifest.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static void ApplyReleaseSource(UmaEventSnapshotData snapshot, string releaseTag)
    {
        snapshot.Manifest.SourceType = ReleaseSourceType;
        snapshot.Manifest.SourceVersion = releaseTag;
        snapshot.Catalog.SourceType = ReleaseSourceType;
        snapshot.Catalog.SourceVersion = releaseTag;
    }

    private static ReleaseDataManifest BuildReleaseManifest(
        UmaEventSnapshotData snapshot,
        CalculatorCharactersDocument characters,
        string releaseTag,
        string channel,
        string generatedAt,
        string? originalSourceVersion,
        int imageCount,
        IReadOnlyList<ReleasePackageAsset> assets)
    {
        return new ReleaseDataManifest
        {
            Locale = snapshot.Manifest.Locale,
            ReleaseTag = releaseTag,
            Channel = channel,
            GeneratorVersion = GeneratorVersion,
            GeneratorCommit = ResolveGitCommit(),
            GeneratedAt = generatedAt,
            SourceVersion = releaseTag,
            OriginalSourceVersion = originalSourceVersion,
            CharacterCount = snapshot.Catalog.CharacterCount,
            VariantCount = snapshot.Catalog.VariantCount,
            EventCount = snapshot.Catalog.EventCount,
            CompatibilityCount = characters.CompatibilityTable.Count,
            ImageCount = imageCount,
            Assets = [.. assets]
        };
    }

    private static ReleaseGenerationReport BuildGenerationReport(
        string outputDir,
        string releaseTag,
        string channel,
        string generatedAt,
        string? originalSourceVersion,
        string calculatorPath,
        string imageManifestPath,
        string assetsRoot,
        int imageCount,
        int reviewItemCount,
        CharacterAssetManifest? imageManifest)
    {
        return new ReleaseGenerationReport
        {
            SchemaVersion = 1,
            ReleaseTag = releaseTag,
            Channel = channel,
            GeneratedAt = generatedAt,
            GeneratorVersion = GeneratorVersion,
            GeneratorCommit = ResolveGitCommit(),
            OutputDirectory = outputDir,
            OriginalSourceVersion = originalSourceVersion,
            CalculatorCharactersPath = calculatorPath,
            ImageManifestPath = imageManifestPath,
            AssetsRoot = assetsRoot,
            ImageCount = imageCount,
            ReviewItemCount = reviewItemCount,
            ImageManifestSummary = imageManifest?.Summary
        };
    }

    private static NeedsHumanReviewDocument BuildReviewDocument(
        CalculatorGenerationReport? calculatorReport,
        CharacterAssetManifest? imageManifest,
        string generatedAt)
    {
        var items = new List<NeedsHumanReviewItem>();

        if (calculatorReport is not null)
        {
            items.AddRange(calculatorReport.NeedsHumanReview.Select((message, index) => new NeedsHumanReviewItem
            {
                Severity = "WARN",
                Source = "calculator",
                Code = "calculator-review",
                Message = message,
                Key = index.ToString("D4")
            }));
        }

        if (imageManifest is not null)
        {
            items.AddRange(imageManifest.Warnings.Select((message, index) => new NeedsHumanReviewItem
            {
                Severity = "INFO",
                Source = "image_manifest",
                Code = "asset-warning",
                Message = message,
                Key = index.ToString("D4")
            }));

            foreach (var entry in imageManifest.Entries.Where(x => x.NeedsHumanReview || x.Status is "missing" or "failed"))
            {
                items.Add(new NeedsHumanReviewItem
                {
                    Severity = entry.Status is "missing" or "failed" ? "WARN" : "INFO",
                    Source = "image_manifest",
                    Code = entry.Status,
                    Message = string.IsNullOrWhiteSpace(entry.Error)
                        ? $"{entry.CharacterId}/{entry.CardId} {entry.NameJa} {entry.VariantNameJa}"
                        : entry.Error,
                    Key = $"{entry.CharacterId}/{entry.CardId}"
                });
            }
        }

        return new NeedsHumanReviewDocument
        {
            Summary = new NeedsHumanReviewSummary
            {
                SchemaVersion = 1,
                GeneratedAt = generatedAt,
                InfoCount = items.Count(x => x.Severity == "INFO"),
                WarnCount = items.Count(x => x.Severity == "WARN"),
                BlockCount = items.Count(x => x.Severity == "BLOCK"),
                ItemCount = items.Count
            },
            Items = items
                .OrderByDescending(x => x.Severity == "BLOCK")
                .ThenByDescending(x => x.Severity == "WARN")
                .ThenBy(x => x.Source, StringComparer.Ordinal)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .ToList()
        };
    }

    private static CharacterAssetManifest? TryReadImageManifest(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<CharacterAssetManifest>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteBrotliJson<T>(string outputPath, T value)
    {
        WriteBrotliBytes(outputPath, SerializeToBytes(value));
    }

    private static void WriteBrotliBytes(string outputPath, byte[] bytes)
    {
        using var file = File.Create(outputPath);
        using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
        brotli.Write(bytes, 0, bytes.Length);
    }

    private static void WriteCharaAssetsZip(string outputPath, IReadOnlyList<string> pngFiles)
    {
        using var file = File.Create(outputPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var pngFile in pngFiles)
        {
            var fileName = Path.GetFileName(pngFile);
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.Contains("..", StringComparison.Ordinal)
                || fileName.Contains(Path.DirectorySeparatorChar)
                || fileName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new InvalidOperationException($"非法 chara asset 文件名: {pngFile}");
            }

            var entry = archive.CreateEntry($"assets/chara/{fileName}", CompressionLevel.Optimal);
            using var source = File.OpenRead(pngFile);
            using var target = entry.Open();
            source.CopyTo(target);
        }
    }

    private static List<ReleasePackageAsset> BuildPackageAssets(string outputDir, IEnumerable<string> fileNames)
    {
        return fileNames
            .Select(fileName =>
            {
                var path = Path.Combine(outputDir, fileName);
                var info = new FileInfo(path);
                return new ReleasePackageAsset
                {
                    FileName = fileName,
                    Sha256 = ComputeSha256(path),
                    SizeBytes = info.Length
                };
            })
            .ToList();
    }

    private static void WriteSha256Sums(string outputPath, IReadOnlyList<ReleasePackageAsset> assets)
    {
        var builder = new StringBuilder();
        foreach (var asset in assets.OrderBy(x => x.FileName, StringComparer.Ordinal))
        {
            builder.Append(asset.Sha256);
            builder.Append("  ");
            builder.Append(asset.FileName);
            builder.AppendLine();
        }

        File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
    }

    private static void WriteJson<T>(string outputPath, T value)
    {
        using var stream = File.Create(outputPath);
        JsonSerializer.Serialize(stream, value, JsonOptions);
    }

    private static T ReadJson<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"无法读取 JSON: {path}");
    }

    private static T Clone<T>(T value)
    {
        return JsonSerializer.Deserialize<T>(SerializeToBytes(value), JsonOptions)
            ?? throw new InvalidOperationException($"无法复制 {typeof(T).Name}");
    }

    private static byte[] SerializeToBytes<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void DeleteKnownPackageFiles(string outputDir)
    {
        foreach (var fileName in PackageFileNames)
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string ResolveGitCommit()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --short=12 HEAD",
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                ? output
                : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string RequireValue(string? value, string optionName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{optionName} 不能为空")
            : value;
    }
}

public sealed class ReleasePackageBuilderOptions
{
    public string OutputDirectory { get; init; } = string.Empty;
    public string ReleaseTag { get; init; } = string.Empty;
    public string Channel { get; init; } = "stable";
    public string CalculatorOutputPath { get; init; } = string.Empty;
    public string? AssetsRoot { get; init; }
    public string? ImageManifestPath { get; init; }
    public bool DryRun { get; init; }
}

public sealed class ReleasePackageResult
{
    public string OutputDirectory { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public ReleaseDataManifest Manifest { get; init; } = new();
    public ReleaseGenerationReport GenerationReport { get; init; } = new();

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("Release package v1 报告");
        Console.WriteLine($"release-out: {OutputDirectory}");
        Console.WriteLine($"release dry-run: {DryRun}");
        Console.WriteLine($"releaseTag: {Manifest.ReleaseTag}");
        Console.WriteLine($"channel: {Manifest.Channel}");
        Console.WriteLine($"characters: {Manifest.CharacterCount}");
        Console.WriteLine($"variants: {Manifest.VariantCount}");
        Console.WriteLine($"events: {Manifest.EventCount}");
        Console.WriteLine($"compatibility_table: {Manifest.CompatibilityCount}");
        Console.WriteLine($"images: {Manifest.ImageCount}");
        Console.WriteLine($"payload assets: {Manifest.Assets.Count}");
    }
}

public sealed class ReleaseDataManifest
{
    public int SchemaVersion { get; set; } = 1;
    public int SnapshotSchemaVersion { get; set; } = 1;
    public int CharactersSchemaVersion { get; set; } = 1;
    public string Locale { get; set; } = "ja-JP";
    public string ReleaseTag { get; set; } = string.Empty;
    public string Channel { get; set; } = "stable";
    public string GeneratorVersion { get; set; } = string.Empty;
    public string? GeneratorCommit { get; set; }
    public string GeneratedAt { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string? OriginalSourceVersion { get; set; }
    public int CharacterCount { get; set; }
    public int VariantCount { get; set; }
    public int EventCount { get; set; }
    public int CompatibilityCount { get; set; }
    public int ImageCount { get; set; }
    public List<ReleasePackageAsset> Assets { get; set; } = [];
}

public sealed class ReleasePackageAsset
{
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed class ReleaseGenerationReport
{
    public int SchemaVersion { get; set; } = 1;
    public string ReleaseTag { get; set; } = string.Empty;
    public string Channel { get; set; } = "stable";
    public string GeneratedAt { get; set; } = string.Empty;
    public string GeneratorVersion { get; set; } = string.Empty;
    public string? GeneratorCommit { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public string? OriginalSourceVersion { get; set; }
    public string CalculatorCharactersPath { get; set; } = string.Empty;
    public string ImageManifestPath { get; set; } = string.Empty;
    public string AssetsRoot { get; set; } = string.Empty;
    public int ImageCount { get; set; }
    public int ReviewItemCount { get; set; }
    public CharacterAssetSummary? ImageManifestSummary { get; set; }
}

public sealed class NeedsHumanReviewDocument
{
    public NeedsHumanReviewSummary Summary { get; set; } = new();
    public List<NeedsHumanReviewItem> Items { get; set; } = [];
}

public sealed class NeedsHumanReviewSummary
{
    public int SchemaVersion { get; set; } = 1;
    public string GeneratedAt { get; set; } = string.Empty;
    public int InfoCount { get; set; }
    public int WarnCount { get; set; }
    public int BlockCount { get; set; }
    public int ItemCount { get; set; }
}

public sealed class NeedsHumanReviewItem
{
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "INFO";

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;
}
