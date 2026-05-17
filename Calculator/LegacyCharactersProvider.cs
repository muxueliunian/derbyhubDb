using System.Text.Encodings.Web;
using System.Text.Json;

namespace derbyhubDb.Calculator;

public sealed class LegacyCharactersProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public LegacyCharactersData Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<LegacyCalculatorCharactersDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"无法读取 legacy characters.json: {path}");

        return new LegacyCharactersData(document);
    }
}

public sealed class LegacyCharactersData
{
    private readonly Dictionary<int, CalculatorCharacter> _charactersById;
    private readonly Dictionary<int, CalculatorCharacterVariant> _variantsByCardId;
    private readonly Dictionary<int, List<CalculatorCharacterVariant>> _variantsByCharacterId;

    public LegacyCharactersData(LegacyCalculatorCharactersDocument document)
    {
        Document = document;
        _charactersById = document.Characters
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());
        _variantsByCardId = document.Characters
            .SelectMany(character => character.Variants)
            .GroupBy(x => x.CardId)
            .ToDictionary(x => x.Key, x => x.First());
        _variantsByCharacterId = document.Characters
            .ToDictionary(x => x.Id, x => x.Variants);
    }

    public LegacyCalculatorCharactersDocument Document { get; }
    public bool IsEmpty => Document.Characters.Count == 0
        && Document.CompatibilityTable.Count == 0;

    public static LegacyCharactersData Empty()
    {
        return new LegacyCharactersData(new LegacyCalculatorCharactersDocument());
    }

    public CalculatorCharacter? FindCharacter(int characterId)
    {
        return _charactersById.GetValueOrDefault(characterId);
    }

    public CalculatorCharacterVariant? FindVariant(int cardId)
    {
        return _variantsByCardId.GetValueOrDefault(cardId);
    }

    public Dictionary<string, int>? FindSameCharacterAptitude(int characterId)
    {
        if (!_variantsByCharacterId.TryGetValue(characterId, out var variants))
        {
            return null;
        }

        var aptitude = variants.FirstOrDefault(x => CalculatorAptitudeKeys.IsComplete(x.Aptitude))?.Aptitude;
        return aptitude is null ? null : CalculatorAptitudeKeys.Clone(aptitude);
    }
}
