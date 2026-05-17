using System.Text.Json.Serialization;

namespace derbyhubDb.UmaEvents;

public sealed class UmaEventSnapshotData
{
    public UmaEventManifestResponse Manifest { get; set; } = new();
    public UmaEventCatalogResponse Catalog { get; set; } = new();
    public Dictionary<int, UmaEventCharacterDetailResponse> Characters { get; set; } = [];
}

public sealed class UmaEventManifestResponse
{
    public string Locale { get; set; } = "ja-JP";
    public string TextVariant { get; set; } = "female";
    public string SourceType { get; set; } = "derbyhub-db";
    public string SourceVersion { get; set; } = string.Empty;
    public string GeneratedAt { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int VariantCount { get; set; }
    public int EventCount { get; set; }
}

public sealed class UmaEventCatalogResponse
{
    public string Locale { get; set; } = "ja-JP";
    public string TextVariant { get; set; } = "female";
    public string SourceType { get; set; } = "derbyhub-db";
    public string SourceVersion { get; set; } = string.Empty;
    public string GeneratedAt { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int VariantCount { get; set; }
    public int EventCount { get; set; }
    public List<UmaEventCatalogCharacterResponse> Characters { get; set; } = [];
}

public sealed class UmaEventCatalogCharacterResponse
{
    public int CharacterId { get; set; }
    public string NameJa { get; set; } = string.Empty;
    public int BaseVariantId { get; set; }
    public int BaseEventCount { get; set; }
    public int CharacterCommonEventCount { get; set; }
    public List<UmaEventVariantSummaryResponse> Variants { get; set; } = [];
}

public sealed class UmaEventCharacterDetailResponse
{
    public int CharacterId { get; set; }
    public string NameJa { get; set; } = string.Empty;
    public int BaseVariantId { get; set; }
    public string GeneratedAt { get; set; } = string.Empty;
    public List<UmaEventVariantSummaryResponse> Variants { get; set; } = [];
    public List<UmaEventEventResponse> BaseEvents { get; set; } = [];
    public List<UmaEventEventResponse> CharacterCommonEvents { get; set; } = [];
    public List<UmaEventVariantDetailResponse> OutfitVariants { get; set; } = [];
}

public sealed class UmaEventVariantSummaryResponse
{
    public int VariantId { get; set; }
    public int EventVariantId { get; set; }
    public int? CardId { get; set; }
    public int? AvatarCardId { get; set; }
    public int? SearchCardId { get; set; }
    public string VariantKind { get; set; } = string.Empty;
    public int? AwakeningLevel { get; set; }
    public string VariantType { get; set; } = string.Empty;
    public string VariantNameJa { get; set; } = string.Empty;
    public int ExclusiveEventCount { get; set; }

    [JsonIgnore]
    public bool HasIdentityFields => EventVariantId != 0 || !string.IsNullOrWhiteSpace(VariantKind);
}

public sealed class UmaEventVariantDetailResponse
{
    public int VariantId { get; set; }
    public string VariantType { get; set; } = string.Empty;
    public string VariantNameJa { get; set; } = string.Empty;
    public int ExclusiveEventCount { get; set; }
    public List<UmaEventEventResponse> ExclusiveEvents { get; set; } = [];
}

public sealed class UmaEventEventResponse
{
    public int EventId { get; set; }
    public string NameJa { get; set; } = string.Empty;
    public string TriggerName { get; set; } = string.Empty;
    public string SourceCategory { get; set; } = string.Empty;
    public bool HasChoice { get; set; }
    public bool HasFailedBranch { get; set; }
    public bool MayContainKiremono { get; set; }
    public string EffectMode { get; set; } = "empty";
    public List<UmaEventChoiceResponse> ChoiceGroups { get; set; } = [];
}

public sealed class UmaEventChoiceResponse
{
    public int OptionIndex { get; set; }
    public string? OptionText { get; set; }
    public UmaEventOutcomeResponse? Success { get; set; }
    public UmaEventOutcomeResponse? Failed { get; set; }
}

public sealed class UmaEventOutcomeResponse
{
    public string? Text { get; set; }
    public UmaEventEffectValuesResponse? Values { get; set; }
    public List<string> SkillNames { get; set; } = [];
    public List<string> Extras { get; set; } = [];
    public string? BuffName { get; set; }
}

public sealed class UmaEventEffectValuesResponse
{
    public int Speed { get; set; }
    public int Stamina { get; set; }
    public int Power { get; set; }
    public int Guts { get; set; }
    public int Wisdom { get; set; }
    public int SkillPt { get; set; }
    public int HintLevel { get; set; }
    public int Vital { get; set; }
    public int Bond { get; set; }
    public int Motivation { get; set; }
}
