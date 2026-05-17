namespace derbyhubDb.MasterDb;

public sealed record TextData(long Category, long Id, long Index, string Text);

public sealed record SupportCardData(
    long Id,
    long CharaId,
    long Rarity,
    long CommandId);

public sealed record SingleModeStoryData(
    long Id,
    long StoryId,
    long ShortStoryId,
    long CardCharaId,
    long CardId,
    long SupportCharaId,
    long SupportCardId,
    long GalleryMainScenario,
    string Name);

public sealed record BaseName(long Id, string Name);

public sealed record UmaName(long Id, string Name, long CharaId);

public sealed class MasterData
{
    public List<TextData> TextData { get; init; } = [];
    public List<SupportCardData> SupportCards { get; init; } = [];
    public List<SingleModeStoryData> Stories { get; init; } = [];
    public Dictionary<long, string> BaseNames { get; init; } = [];
    public Dictionary<long, UmaName> UmaNames { get; init; } = [];
    public Dictionary<long, long> DefaultCardIds { get; init; } = [];
    public Dictionary<long, string> SupportCardNames { get; init; } = [];
    public Dictionary<string, long> NameToId { get; init; } = [];

    public string? TryGetText(long category, long index)
    {
        return TextData.FirstOrDefault(x => x.Category == category && x.Index == index)?.Text;
    }
}
