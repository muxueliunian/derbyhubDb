using derbyhubDb.Effects;
using derbyhubDb.MasterDb;
using derbyhubDb.StoryData;
using derbyhubDb.UmaEvents;

namespace derbyhubDb.Output;

public sealed class GenerationReport
{
    public int CharacterCount { get; init; }
    public int VariantCount { get; init; }
    public int EventCount { get; init; }
    public int OutfitCount { get; init; }
    public int MissingEffectCount { get; init; }
    public int UnclassifiedEventCount { get; init; }
    public int VariantIdentityWarningCount { get; init; }
    public int VariantIdentityBlockCount { get; init; }
    public List<string> VariantIdentityWarnings { get; init; } = [];
    public List<string> VariantIdentityBlocks { get; init; } = [];
    public int ScannedStoryFileCount { get; init; }
    public int MissingMasterStoryCount { get; init; }
    public string? KamigameWarning { get; init; }
    public int KamigameMatchedEventCount { get; init; }
    public int KamigameUnmatchedEventCount { get; init; }

    public static GenerationReport From(
        MasterData master,
        StoryReadResult storyResult,
        SnapshotBuildResult buildResult,
        EffectLoadResult effectResult)
    {
        return new GenerationReport
        {
            CharacterCount = buildResult.CharacterCount,
            VariantCount = buildResult.VariantCount,
            OutfitCount = master.UmaNames.Count,
            EventCount = buildResult.EventCount,
            MissingEffectCount = storyResult.MissingEffectCount,
            UnclassifiedEventCount = buildResult.UnclassifiedEvents.Count,
            VariantIdentityWarningCount = buildResult.VariantIdentityWarnings.Count,
            VariantIdentityBlockCount = buildResult.VariantIdentityBlocks.Count,
            VariantIdentityWarnings = buildResult.VariantIdentityWarnings,
            VariantIdentityBlocks = buildResult.VariantIdentityBlocks,
            ScannedStoryFileCount = storyResult.ScannedFileCount,
            MissingMasterStoryCount = storyResult.MissingMasterStoryCount,
            KamigameWarning = effectResult.Warning,
            KamigameMatchedEventCount = effectResult.MatchedEventCount,
            KamigameUnmatchedEventCount = effectResult.UnmatchedEventCount
        };
    }

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("生成报告");
        Console.WriteLine($"角色数: {CharacterCount}");
        Console.WriteLine($"衣装数: {VariantCount}");
        Console.WriteLine($"事件数: {EventCount}");
        Console.WriteLine($"扫描 storytimeline 文件数: {ScannedStoryFileCount}");
        Console.WriteLine($"无法匹配收益的事件数: {MissingEffectCount}");
        Console.WriteLine($"无法分类事件数: {UnclassifiedEventCount}");
        Console.WriteLine($"variant 身份 warning 数: {VariantIdentityWarningCount}");
        Console.WriteLine($"variant 身份 block 数: {VariantIdentityBlockCount}");
        Console.WriteLine($"master 中缺失的 story 数: {MissingMasterStoryCount}");
        Console.WriteLine($"Kamigame 已匹配 story 数: {KamigameMatchedEventCount}");
        Console.WriteLine($"Kamigame 未匹配行数: {KamigameUnmatchedEventCount}");
        if (!string.IsNullOrWhiteSpace(KamigameWarning))
        {
            Console.WriteLine($"Kamigame 警告: {KamigameWarning}");
        }

        PrintPreview("variant 身份 warning", VariantIdentityWarnings);
        PrintPreview("variant 身份 block", VariantIdentityBlocks);
    }

    private static void PrintPreview(string title, IReadOnlyCollection<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        Console.WriteLine($"{title} 前 20 条:");
        foreach (var item in items.Take(20))
        {
            Console.WriteLine($"  {item}");
        }
    }
}
