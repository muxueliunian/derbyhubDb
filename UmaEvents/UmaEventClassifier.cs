using derbyhubDb.MasterDb;

namespace derbyhubDb.UmaEvents;

public sealed class UmaEventClassifier
{
    private const int BaseVariantMultiplier = 100;

    private readonly Dictionary<string, int> _baseTriggerMap = [];
    private readonly Dictionary<string, VariantDefinition> _outfitTriggerMap = [];

    public UmaEventClassifier(MasterData master)
    {
        foreach (var item in master.BaseNames)
        {
            _baseTriggerMap[$"[角色]{item.Value}"] = (int)item.Key;
        }

        foreach (var item in master.UmaNames.Values)
        {
            if (!master.BaseNames.TryGetValue(item.CharaId, out var baseName))
            {
                continue;
            }

            _outfitTriggerMap[$"{item.Name}{baseName}"] = new VariantDefinition((int)item.CharaId, (int)item.Id, "outfit", item.Name);
        }
    }

    public int BaseVariantId(int characterId)
    {
        return characterId * BaseVariantMultiplier;
    }

    public EventClassification Classify(long eventId, string triggerName)
    {
        if (triggerName == "[角色]通用事件")
        {
            var characterId = (int)((eventId / 1000) % 10000);
            return new EventClassification(characterId, null, "character_common");
        }

        if (_baseTriggerMap.TryGetValue(triggerName, out var baseCharacterId))
        {
            return new EventClassification(baseCharacterId, null, "base");
        }

        if (_outfitTriggerMap.TryGetValue(triggerName, out var outfit))
        {
            return new EventClassification(outfit.CharacterId, outfit, "outfit_exclusive");
        }

        return EventClassification.Unknown;
    }

    public IEnumerable<VariantDefinition> OutfitDefinitions => _outfitTriggerMap.Values
        .GroupBy(x => x.VariantId)
        .Select(x => x.First());
}

public sealed record VariantDefinition(int CharacterId, int VariantId, string VariantType, string VariantNameJa);

public sealed record EventClassification(int CharacterId, VariantDefinition? Variant, string SourceCategory)
{
    public static EventClassification Unknown { get; } = new(0, null, "unknown");
    public bool IsKnown => CharacterId != 0 && SourceCategory != "unknown";
}
