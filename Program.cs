using System.Text.Encodings.Web;
using System.Text.Json;
using derbyhubDb.Assets;
using derbyhubDb.Calculator;
using derbyhubDb.Cli;
using derbyhubDb.Effects;
using derbyhubDb.MasterDb;
using derbyhubDb.Output;
using derbyhubDb.Release;
using derbyhubDb.StoryData;
using derbyhubDb.UmaEvents;

Console.OutputEncoding = System.Text.Encoding.UTF8;

try
{
    var options = CliOptions.Parse(args);
    options.Validate();

    Console.WriteLine("DerbyHub uma-events snapshot generator");
    Console.WriteLine($"master.mdb: {options.MasterMdb}");
    Console.WriteLine($"story-data: {options.StoryData}");
    Console.WriteLine($"snapshot-in:{options.SnapshotIn ?? "(none)"}");
    Console.WriteLine($"out:        {options.Out}");
    Console.WriteLine($"dry-run:    {options.DryRun}");
    Console.WriteLine($"calculator-out: {options.CalculatorOut ?? "(none)"}");
    Console.WriteLine($"release-out:    {options.ReleaseOut ?? "(none)"}");

    UmaEventSnapshotData snapshot;
    CharacterAssetGenerationResult? assetResult = null;
    CalculatorGenerationResult? calculatorResult = null;

    if (options.UseSnapshotInput)
    {
        snapshot = ReadSnapshot(options.SnapshotIn!);
        Console.WriteLine($"snapshot loaded: {Path.GetFullPath(options.SnapshotIn!)}");
    }
    else
    {
        var master = MasterDbReader.Read(options.MasterMdb);
        var corrections = CorrectionTables.Load(options.CorrectionsDir);
        var effectProvider = new KamigameEffectProvider();
        var effectResult = await effectProvider.LoadAsync(options.KamigameUrl, master, corrections);

        var storyReader = new StoryTimelineReader();
        var storyResult = storyReader.Read(options.StoryData, master, effectResult.EffectsByStoryId);

        var sourceVersion = SourceVersionHasher.Compute(
            options.MasterMdb,
            options.StoryData,
            options.CorrectionsDir);

        var builder = new UmaEventSnapshotBuilder();
        var buildResult = builder.Build(master, storyResult.Stories, sourceVersion, DateTimeOffset.UtcNow);
        snapshot = buildResult.Snapshot;

        var report = GenerationReport.From(
            master,
            storyResult,
            buildResult,
            effectResult);

        if (options.DebugJson)
        {
            var debugDir = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(options.Out)) ?? Environment.CurrentDirectory,
                "debug");
            Directory.CreateDirectory(debugDir);
            DebugJsonWriter.Write(debugDir, storyResult, effectResult);
            Console.WriteLine($"debug-json: {debugDir}");
        }

        if (!options.DryRun && ((!options.AssetGenerationRequested && !options.CalculatorGenerationRequested) || options.OutExplicit))
        {
            SnapshotWriter.Write(options.Out, snapshot);
        }
        else if ((options.AssetGenerationRequested || options.CalculatorGenerationRequested) && !options.OutExplicit)
        {
            Console.WriteLine("snapshot write skipped: generated-data run without explicit --out");
        }

        report.Print();
    }

    if (options.AssetGenerationRequested)
    {
        var assetDryRun = options.AssetsDryRun || options.DryRun;
        var assetTarget = options.ResolveAssetsTargetRoot();
        Console.WriteLine();
        Console.WriteLine("DerbyHub character image asset generator");
        Console.WriteLine($"assets-out:       {assetTarget}");
        Console.WriteLine($"derbyhub-public:  {options.DerbyhubPublic ?? "(none)"}");
        Console.WriteLine($"game-data-root:   {options.GameDataRoot}");
        Console.WriteLine($"meta-db:          {options.MetaDb ?? "(auto)"}");
        Console.WriteLine($"sqlite3mc-dll:    {options.Sqlite3McDll ?? "(auto/env/tools)"}");
        Console.WriteLine($"download-missing: {options.DownloadMissing}");
        Console.WriteLine($"assets dry-run:   {assetDryRun}");
        Console.WriteLine($"force-assets:     {options.ForceAssets}");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        var generator = new CharacterAssetGenerator(
            new GameAssetProbe(),
            new CharacterImageResolver(new GameToraImageDownloader(httpClient), new UnityCharacterImageExtractor()));
        assetResult = await generator.GenerateAsync(
            snapshot,
            new CharacterAssetGeneratorOptions
            {
                TargetRoot = assetTarget,
                DerbyhubPublic = options.DerbyhubPublic,
                GameDataRoot = options.GameDataRoot,
                MasterMdb = options.MasterMdb,
                MetaDb = options.MetaDb,
                Sqlite3McDll = options.Sqlite3McDll,
                DownloadMissing = options.DownloadMissing,
                DryRun = assetDryRun,
                Force = options.ForceAssets
            });
        assetResult.Print();
    }

    if (options.CalculatorGenerationRequested)
    {
        var calculatorDryRun = options.CalculatorDryRun || options.DryRun;
        var calculatorOutputPath = options.ResolveCalculatorOutputPath();
        var calculatorTargetRoot = options.ResolveCalculatorTargetRoot();
        var legacyPublicRoot = ResolveLegacyPublicRoot(options.LegacyCharactersJson);
        Console.WriteLine();
        Console.WriteLine("DerbyHub calculator characters.json generator");
        Console.WriteLine($"calculator-out:          {calculatorOutputPath}");
        Console.WriteLine($"calculator-target-root:  {calculatorTargetRoot}");
        Console.WriteLine($"calculator-source:       {options.CalculatorSource}");
        Console.WriteLine($"legacy-characters-json:  {options.LegacyCharactersJson}");
        Console.WriteLine($"calculator dry-run:      {calculatorDryRun}");

        var generator = new CalculatorDataGenerator();
        calculatorResult = generator.Generate(
            snapshot,
            new CalculatorDataGeneratorOptions
            {
                MasterMdb = options.MasterMdb,
                OutputPath = calculatorOutputPath,
                TargetRoot = calculatorTargetRoot,
                LegacyCharactersJson = options.LegacyCharactersJson,
                DerbyhubPublic = options.DerbyhubPublic,
                LegacyPublicRoot = legacyPublicRoot,
                SourceMode = options.ResolveCalculatorSource() ?? CalculatorSourceMode.Auto,
                DryRun = calculatorDryRun
            });
        calculatorResult.Report.Print();
    }

    if (options.ReleasePackageRequested)
    {
        Console.WriteLine();
        Console.WriteLine("DerbyHub release package v1 generator");
        Console.WriteLine($"release-out:     {options.ReleaseOut}");
        Console.WriteLine($"release-tag:     {options.ReleaseTag}");
        Console.WriteLine($"release-channel: {options.ReleaseChannel}");
        Console.WriteLine($"release dry-run: {options.ReleaseDryRun || options.DryRun}");

        var releaseResult = new ReleasePackageBuilder().Build(
            snapshot,
            new ReleasePackageBuilderOptions
            {
                OutputDirectory = options.ReleaseOut!,
                ReleaseTag = options.ReleaseTag!,
                Channel = options.ReleaseChannel,
                CalculatorOutputPath = options.CalculatorOut!,
                AssetsRoot = options.AssetsOut,
                DryRun = options.ReleaseDryRun || options.DryRun
            },
            calculatorResult,
            assetResult);
        releaseResult.Print();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"生成失败: {ex.Message}");
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

static UmaEventSnapshotData ReadSnapshot(string path)
{
    var jsonOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    using var stream = File.OpenRead(path);
    return JsonSerializer.Deserialize<UmaEventSnapshotData>(stream, jsonOptions)
        ?? throw new InvalidOperationException($"无法读取 snapshot: {path}");
}

static string? ResolveLegacyPublicRoot(string legacyCharactersJson)
{
    var fullPath = Path.GetFullPath(legacyCharactersJson);
    var dataDir = Path.GetDirectoryName(fullPath);
    if (dataDir is null || !Path.GetFileName(dataDir).Equals("data", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    return Path.GetDirectoryName(dataDir);
}
