using System.Text.Json.Serialization;
using derbyhubDb.Effects;

namespace derbyhubDb.StoryData;

public sealed class StoryTimeline
{
    public string Title { get; set; } = string.Empty;
    public List<TextBlock> TextBlockList { get; set; } = [];
}

public sealed class TextBlock
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<ChoiceData> ChoiceDataList { get; set; } = [];
    public List<string> ColorTextInfoList { get; set; } = [];
}

public sealed class ChoiceData
{
    public string Text { get; set; } = string.Empty;
    public int NextBlock { get; set; }
    public int DifferenceFlag { get; set; }

    [JsonIgnore]
    public bool IsMale => DifferenceFlag == 2;

    [JsonIgnore]
    public bool IsFemale => DifferenceFlag == 4;
}

public sealed class StoryEvent
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TriggerName { get; init; } = string.Empty;
    public List<List<Choice>> Choices { get; init; } = [];
}

public sealed class StoryReadResult
{
    public List<StoryEvent> Stories { get; init; } = [];
    public int ScannedFileCount { get; init; }
    public int MissingMasterStoryCount { get; init; }
    public int MissingEffectCount { get; init; }
    public List<string> MissingMasterStories { get; init; } = [];
}
