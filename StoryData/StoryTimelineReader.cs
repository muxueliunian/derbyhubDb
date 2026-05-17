using System.Text.Json;
using derbyhubDb.Effects;
using derbyhubDb.MasterDb;

namespace derbyhubDb.StoryData;

public sealed class StoryTimelineReader
{
    public StoryReadResult Read(string storyDataDir, MasterData master, IReadOnlyDictionary<long, List<List<Choice>>> effectsByStoryId)
    {
        var characterDir = Path.Combine(storyDataDir, "50");
        if (!Directory.Exists(characterDir))
        {
            throw new DirectoryNotFoundException($"找不到角色事件目录: {characterDir}");
        }

        var stories = new List<StoryEvent>();
        var missingMaster = new List<string>();
        var missingEffectCount = 0;
        var files = Directory.EnumerateFiles(characterDir, "storytimeline_*.json", SearchOption.AllDirectories).ToList();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in files)
        {
            var storyId = ParseStoryId(file);
            var masterStory = master.Stories.FirstOrDefault(x => x.StoryId == storyId || x.ShortStoryId == storyId);
            if (masterStory is null)
            {
                missingMaster.Add($"{storyId}: {file}");
                continue;
            }

            var timeline = JsonSerializer.Deserialize<StoryTimeline>(File.ReadAllText(file), options);
            if (timeline is null)
            {
                continue;
            }

            var choices = BuildChoices(timeline, effectsByStoryId.GetValueOrDefault(storyId));
            if (!effectsByStoryId.ContainsKey(storyId))
            {
                missingEffectCount++;
            }

            stories.Add(new StoryEvent
            {
                Id = storyId,
                Name = string.IsNullOrWhiteSpace(timeline.Title) ? masterStory.Name : timeline.Title,
                TriggerName = ResolveTriggerName(master, masterStory),
                Choices = choices
            });
        }

        return new StoryReadResult
        {
            Stories = stories
                .GroupBy(x => x.Id)
                .Select(x => x.First())
                .OrderBy(x => x.Id)
                .ToList(),
            ScannedFileCount = files.Count,
            MissingMasterStoryCount = missingMaster.Count,
            MissingEffectCount = missingEffectCount,
            MissingMasterStories = missingMaster.Take(200).ToList()
        };
    }

    private static long ParseStoryId(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (!name.StartsWith("storytimeline_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"无法解析 storytimeline 文件名: {file}");
        }

        return long.Parse(name["storytimeline_".Length..]);
    }

    private static string ResolveTriggerName(MasterData master, SingleModeStoryData storyData)
    {
        if (storyData.CardId == 0)
        {
            if (storyData.CardCharaId == 0)
            {
                return "[角色]通用事件";
            }

            return master.BaseNames.TryGetValue(storyData.CardCharaId, out var baseName)
                ? $"[角色]{baseName}"
                : "[角色]通用事件";
        }

        if (master.UmaNames.TryGetValue(storyData.CardId, out var umaName)
            && master.BaseNames.TryGetValue(umaName.CharaId, out var baseCharacterName))
        {
            return $"{umaName.Name}{baseCharacterName}";
        }

        return master.TryGetText(4, storyData.CardId) ?? $"[未知衣装]{storyData.CardId}";
    }

    private static List<List<Choice>> BuildChoices(StoryTimeline timeline, List<List<Choice>>? matchedEffects)
    {
        var textBlock = timeline.TextBlockList.FirstOrDefault(x =>
            x is not null
            && x.ChoiceDataList.Count >= 2
            && x.ChoiceDataList.Select(y => y.NextBlock).Distinct().Count() >= 2);

        if (textBlock is null)
        {
            return matchedEffects ?? [[new Choice { Option = "无选项" }]];
        }

        var groups = textBlock.ChoiceDataList
            .GroupBy(x => x.NextBlock)
            .ToList();

        var result = new List<List<Choice>>();
        for (var i = 0; i < groups.Count; i++)
        {
            var option = SelectFemaleOrDefault(groups[i].ToList());
            var effectChoice = matchedEffects is not null && matchedEffects.Count > i && matchedEffects[i].Count > 0
                ? matchedEffects[i][0]
                : null;

            result.Add(
            [
                new Choice
                {
                    Option = string.IsNullOrWhiteSpace(option) ? effectChoice?.Option ?? string.Empty : option,
                    SuccessEffect = effectChoice?.SuccessEffect ?? string.Empty,
                    FailedEffect = effectChoice?.FailedEffect ?? string.Empty,
                    SuccessEffectValue = effectChoice?.SuccessEffectValue,
                    FailedEffectValue = effectChoice?.FailedEffectValue
                }
            ]);
        }

        return result;
    }

    private static string SelectFemaleOrDefault(IReadOnlyList<ChoiceData> choices)
    {
        return choices.FirstOrDefault(x => x.IsFemale)?.Text
            ?? choices.FirstOrDefault()?.Text
            ?? string.Empty;
    }
}
