namespace derbyhubDb.Cli;

public sealed class CliOptions
{
    public const string DefaultMasterMdb = @"G:\DMM\Umamusume\umamusume_Data\Persistent\master\master.mdb";
    public const string DefaultStoryData = @"C:\Users\atlas\Desktop\uma_web_3\uma-story-extracted\story\data";
    public const string DefaultOut = @"C:\Users\atlas\Desktop\uma_web_3\DerbyHub\backend\derbyhub-api\data\uma-events\ja-JP\current\snapshot.json";
    public const string DefaultKamigameUrl = "https://kamigame.jp/vls-kamigame-gametool/json/1JrYvw5XiwWeKR5c2BKVQykutI_Lj2_zauLvaWtnzvDo_411452117.json";
    public const string DefaultGameDataRoot = @"G:\DMM\Umamusume\umamusume_Data";
    public const string DefaultLegacyCharactersJson = @"C:\Users\atlas\Desktop\uma_web_3\DerbyHub\frontend\derbyhub-ui\public\data\characters.json";

    public string MasterMdb { get; private init; } = DefaultMasterMdb;
    public string StoryData { get; private init; } = DefaultStoryData;
    public string Out { get; private init; } = DefaultOut;
    public string? SnapshotIn { get; private init; }
    public string? CorrectionsDir { get; private init; }
    public string? KamigameUrl { get; private init; } = DefaultKamigameUrl;
    public string? AssetsOut { get; private init; }
    public string? DerbyhubPublic { get; private init; }
    public string? CalculatorOut { get; private init; }
    public string? ReleaseOut { get; private init; }
    public string? ReleaseTag { get; private init; }
    public string ReleaseChannel { get; private init; } = "stable";
    public string CalculatorSource { get; private init; } = "auto";
    public string LegacyCharactersJson { get; private init; } = DefaultLegacyCharactersJson;
    public string GameDataRoot { get; private init; } = DefaultGameDataRoot;
    public string? MetaDb { get; private init; }
    public string? Sqlite3McDll { get; private init; }
    public bool OutExplicit { get; private init; }
    public bool DebugJson { get; private init; }
    public bool DryRun { get; private init; }
    public bool DownloadMissing { get; private init; }
    public bool AssetsDryRun { get; private init; }
    public bool ForceAssets { get; private init; }
    public bool CalculatorDryRun { get; private init; }
    public bool ReleaseDryRun { get; private init; }

    public bool ReleasePackageRequested => ReleaseOut is not null;

    public bool AssetGenerationRequested =>
        (!ReleasePackageRequested && AssetsOut is not null)
        || DerbyhubPublic is not null
        || DownloadMissing
        || AssetsDryRun
        || ForceAssets;

    public bool CalculatorGenerationRequested =>
        (!ReleasePackageRequested && CalculatorOut is not null)
        || CalculatorDryRun;

    public bool UseSnapshotInput => !string.IsNullOrWhiteSpace(SnapshotIn);

    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"无法识别的参数: {arg}");
            }

            var key = arg[2..];
            if (key is "debug-json" or "dry-run" or "download-missing" or "assets-dry-run" or "force-assets" or "calculator-dry-run" or "release-dry-run")
            {
                flags.Add(key);
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"参数 --{key} 需要一个值");
            }

            values[key] = args[++i];
        }

        return new CliOptions
        {
            MasterMdb = Get(values, "master-mdb", DefaultMasterMdb),
            StoryData = Get(values, "story-data", DefaultStoryData),
            Out = Get(values, "out", DefaultOut),
            SnapshotIn = GetNullable(values, "snapshot-in"),
            CorrectionsDir = GetNullable(values, "corrections-dir"),
            KamigameUrl = GetNullable(values, "kamigame-url") ?? DefaultKamigameUrl,
            AssetsOut = GetNullable(values, "assets-out"),
            DerbyhubPublic = GetNullable(values, "derbyhub-public"),
            CalculatorOut = GetNullable(values, "calculator-out"),
            ReleaseOut = GetNullable(values, "release-out"),
            ReleaseTag = GetNullable(values, "release-tag"),
            ReleaseChannel = Get(values, "release-channel", "stable").ToLowerInvariant(),
            CalculatorSource = Get(values, "calculator-source", "auto"),
            LegacyCharactersJson = Get(values, "legacy-characters-json", DefaultLegacyCharactersJson),
            GameDataRoot = Get(values, "game-data-root", DefaultGameDataRoot),
            MetaDb = GetNullable(values, "meta-db"),
            Sqlite3McDll = GetNullable(values, "sqlite3mc-dll"),
            OutExplicit = values.ContainsKey("out"),
            DebugJson = flags.Contains("debug-json"),
            DryRun = flags.Contains("dry-run"),
            DownloadMissing = flags.Contains("download-missing"),
            AssetsDryRun = flags.Contains("assets-dry-run"),
            ForceAssets = flags.Contains("force-assets"),
            CalculatorDryRun = flags.Contains("calculator-dry-run"),
            ReleaseDryRun = flags.Contains("release-dry-run")
        };
    }

    public void Validate()
    {
        if (UseSnapshotInput)
        {
            if (!File.Exists(SnapshotIn))
            {
                throw new FileNotFoundException("找不到 snapshot-in", SnapshotIn);
            }
        }
        else if (!File.Exists(MasterMdb))
        {
            throw new FileNotFoundException("找不到 master.mdb", MasterMdb);
        }

        if (!UseSnapshotInput && !Directory.Exists(StoryData))
        {
            throw new DirectoryNotFoundException($"找不到 story-data 目录: {StoryData}");
        }

        if (!string.IsNullOrWhiteSpace(CorrectionsDir) && !Directory.Exists(CorrectionsDir))
        {
            throw new DirectoryNotFoundException($"找不到 corrections-dir 目录: {CorrectionsDir}");
        }

        var outDir = Path.GetDirectoryName(Path.GetFullPath(Out));
        if (string.IsNullOrWhiteSpace(outDir))
        {
            throw new ArgumentException("--out 必须指向一个文件路径");
        }

        if (AssetGenerationRequested)
        {
            if (string.IsNullOrWhiteSpace(ResolveAssetsTargetRoot()))
            {
                throw new ArgumentException("资产输出目录无效");
            }

            if (!string.IsNullOrWhiteSpace(DerbyhubPublic) && !Directory.Exists(DerbyhubPublic))
            {
                throw new DirectoryNotFoundException($"找不到 derbyhub-public 目录: {DerbyhubPublic}");
            }

            if (!string.IsNullOrWhiteSpace(MetaDb) && !File.Exists(MetaDb))
            {
                throw new FileNotFoundException("找不到 meta-db", MetaDb);
            }

            if (!string.IsNullOrWhiteSpace(Sqlite3McDll) && !File.Exists(Sqlite3McDll))
            {
                throw new FileNotFoundException("找不到 sqlite3mc-dll", Sqlite3McDll);
            }
        }

        if (CalculatorGenerationRequested)
        {
            if (ResolveCalculatorSource() is null)
            {
                throw new ArgumentException("--calculator-source 必须是 local、legacy-fallback 或 auto");
            }

            if (string.IsNullOrWhiteSpace(CalculatorOut))
            {
                throw new ArgumentException("--calculator-dry-run 需要同时提供 --calculator-out，用于解析目标目录和校验图片路径");
            }

            if (ResolveCalculatorSource() == CalculatorSourceMode.LegacyFallback && !File.Exists(LegacyCharactersJson))
            {
                throw new FileNotFoundException("找不到 legacy characters.json", LegacyCharactersJson);
            }
        }

        if (ReleasePackageRequested)
        {
            if (string.IsNullOrWhiteSpace(ReleaseTag))
            {
                throw new ArgumentException("--release-out 需要同时提供 --release-tag");
            }

            if (ReleaseChannel is not "stable" and not "prerelease")
            {
                throw new ArgumentException("--release-channel 必须是 stable 或 prerelease");
            }

            if (string.IsNullOrWhiteSpace(ReleaseOut))
            {
                throw new ArgumentException("--release-out 必须指向输出目录");
            }

            if (string.IsNullOrWhiteSpace(CalculatorOut))
            {
                throw new ArgumentException("--release-out 需要 --calculator-out 指向已有 calculator-data 目录或 characters.json");
            }
        }
    }

    public string ResolveAssetsTargetRoot()
    {
        if (!string.IsNullOrWhiteSpace(AssetsOut))
        {
            return AssetsOut;
        }

        if (!string.IsNullOrWhiteSpace(DerbyhubPublic) && !AssetsDryRun)
        {
            return DerbyhubPublic;
        }

        return Path.Combine(Environment.CurrentDirectory, "tmp", "frontend-assets");
    }

    public string ResolveCalculatorOutputPath()
    {
        if (string.IsNullOrWhiteSpace(CalculatorOut))
        {
            throw new ArgumentException("--calculator-out 必须指向目录或 characters.json 文件");
        }

        var fullPath = Path.GetFullPath(CalculatorOut);
        if (Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return Path.Combine(fullPath, "data", "characters.json");
    }

    public string ResolveCalculatorTargetRoot()
    {
        var outputPath = ResolveCalculatorOutputPath();
        var fileName = Path.GetFileName(outputPath);
        var dataDir = Path.GetDirectoryName(outputPath) ?? throw new ArgumentException("--calculator-out 输出路径无效");
        if (fileName.Equals("characters.json", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(dataDir).Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(dataDir) ?? dataDir;
        }

        return dataDir;
    }

    public CalculatorSourceMode? ResolveCalculatorSource()
    {
        return CalculatorSource.ToLowerInvariant() switch
        {
            "local" => CalculatorSourceMode.Local,
            "legacy-fallback" => CalculatorSourceMode.LegacyFallback,
            "auto" => CalculatorSourceMode.Auto,
            _ => null
        };
    }

    private static string Get(IReadOnlyDictionary<string, string?> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string? GetNullable(IReadOnlyDictionary<string, string?> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}

public enum CalculatorSourceMode
{
    Local,
    LegacyFallback,
    Auto
}
