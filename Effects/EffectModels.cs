namespace derbyhubDb.Effects;

public sealed class EffectValue
{
    public int[] Values { get; init; } = new int[10];
    public string? BuffName { get; set; }
    public List<string> SkillNames { get; init; } = [];
    public List<string> Extras { get; init; } = [];
}

public sealed class Choice
{
    public string Option { get; set; } = string.Empty;
    public string SuccessEffect { get; set; } = string.Empty;
    public string FailedEffect { get; set; } = string.Empty;
    public EffectValue? SuccessEffectValue { get; set; }
    public EffectValue? FailedEffectValue { get; set; }
}

public sealed class EffectLoadResult
{
    public Dictionary<long, List<List<Choice>>> EffectsByStoryId { get; init; } = [];
    public int KamigameRowCount { get; init; }
    public int MatchedEventCount { get; init; }
    public int UnmatchedEventCount { get; init; }
    public List<string> UnmatchedEvents { get; init; } = [];
    public string? Warning { get; init; }
}
