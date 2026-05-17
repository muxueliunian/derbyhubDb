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
        Console.WriteLine($"master 中缺失的 story 数: {MissingMasterStoryCount}");
        Console.WriteLine($"Kamigame 已匹配 story 数: {KamigameMatchedEventCount}");
        Console.WriteLine($"Kamigame 未匹配行数: {KamigameUnmatchedEventCount}");
        if (!string.IsNullOrWhiteSpace(KamigameWarning))
        {
            Console.WriteLine($"Kamigame 警告: {KamigameWarning}");
        }
    }
}
