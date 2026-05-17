using System.Text.Json.Serialization;

namespace derbyhubDb.Calculator;

public sealed class CalculatorCharactersDocument
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "derbyhubDb-calculator-v1";

    [JsonPropertyName("characters")]
    public List<CalculatorCharacter> Characters { get; set; } = [];

    [JsonPropertyName("compatibility_table")]
    public Dictionary<string, int> CompatibilityTable { get; set; } = [];

    [JsonPropertyName("tag_compatibility")]
    public Dictionary<string, int> TagCompatibility { get; set; } = [];

    [JsonPropertyName("grade_thresholds")]
    public Dictionary<string, int> GradeThresholds { get; set; } = new()
    {
        ["double_circle"] = 51,
        ["circle"] = 21,
        ["triangle"] = 6,
        ["cross"] = 0
    };
}

public sealed class CalculatorCharacter
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name_ja")]
    public string NameJa { get; set; } = string.Empty;

    [JsonPropertyName("name_zh")]
    public string NameZh { get; set; } = string.Empty;

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("variants")]
    public List<CalculatorCharacterVariant> Variants { get; set; } = [];

    [JsonPropertyName("compatibility_tags")]
    public List<string> CompatibilityTags { get; set; } = [];
}

public sealed class CalculatorCharacterVariant
{
    [JsonPropertyName("card_id")]
    public int CardId { get; set; }

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;

    [JsonPropertyName("aptitude")]
    public Dictionary<string, int> Aptitude { get; set; } = [];
}

public sealed class LegacyCalculatorCharactersDocument
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("characters")]
    public List<CalculatorCharacter> Characters { get; set; } = [];

    [JsonPropertyName("compatibility_table")]
    public Dictionary<string, int> CompatibilityTable { get; set; } = [];

    [JsonPropertyName("tag_compatibility")]
    public Dictionary<string, int>? TagCompatibility { get; set; }

    [JsonPropertyName("grade_thresholds")]
    public Dictionary<string, int>? GradeThresholds { get; set; }
}

public static class CalculatorAptitudeKeys
{
    public static readonly string[] All =
    [
        "turf",
        "dirt",
        "short",
        "mile",
        "middle",
        "long",
        "nige",
        "senko",
        "sashi",
        "oikomi"
    ];

    public static bool IsComplete(IReadOnlyDictionary<string, int>? aptitude)
    {
        return aptitude is not null
            && All.All(key => aptitude.TryGetValue(key, out var value) && value is >= 1 and <= 7);
    }

    public static Dictionary<string, int> ConservativeDefault()
    {
        return All.ToDictionary(key => key, _ => 7, StringComparer.Ordinal);
    }

    public static Dictionary<string, int> Clone(IReadOnlyDictionary<string, int> aptitude)
    {
        return All.ToDictionary(key => key, key => aptitude[key], StringComparer.Ordinal);
    }
}
