using derbyhubDb.Effects;
using derbyhubDb.MasterDb;
using derbyhubDb.StoryData;

namespace derbyhubDb.UmaEvents;

public sealed class UmaEventSnapshotBuilder
{
    private static readonly string[] KiremonoAliases = ["切れ者", "切れ物", "切者", "能人"];

    public SnapshotBuildResult Build(MasterData master, IReadOnlyCollection<StoryEvent> stories, string sourceVersion, DateTimeOffset generatedAt)
    {
        var classifier = new UmaEventClassifier(master);
        var characters = master.BaseNames
            .ToDictionary(
                x => (int)x.Key,
                x => new CharacterAccumulator((int)x.Key, x.Value, classifier.BaseVariantId((int)x.Key)));

        foreach (var outfit in classifier.OutfitDefinitions)
        {
            if (characters.TryGetValue(outfit.CharacterId, out var character))
            {
                character.RegisterVariant(outfit);
            }
        }

        var unclassified = new List<StoryEvent>();
        foreach (var story in stories)
        {
            var classification = classifier.Classify(story.Id, story.TriggerName);
            if (!classification.IsKnown || !characters.TryGetValue(classification.CharacterId, out var character))
            {
                unclassified.Add(story);
                continue;
            }

            var eventResponse = BuildEvent(story, classification.SourceCategory);
            switch (classification.SourceCategory)
            {
                case "base":
                    character.AddBaseEvent(eventResponse);
                    break;
                case "character_common":
                    character.AddCommonEvent(eventResponse);
                    break;
                case "outfit_exclusive":
                    if (classification.Variant is not null)
                    {
                        character.AddOutfitExclusiveEvent(classification.Variant.VariantId, eventResponse);
                    }
                    break;
            }
        }

        return FinalizeSnapshot(characters, generatedAt, sourceVersion, unclassified);
    }

    private static UmaEventEventResponse BuildEvent(StoryEvent story, string sourceCategory)
    {
        var response = new UmaEventEventResponse
        {
            EventId = (int)story.Id,
            NameJa = story.Name,
            TriggerName = story.TriggerName,
            SourceCategory = sourceCategory
        };

        var hasChoice = false;
        var hasFailedBranch = false;
        var hasStructured = false;
        var hasFixed = false;
        var hasTextOnly = false;
        var mayContainKiremono = false;

        for (var i = 0; i < story.Choices.Count; i++)
        {
            var choice = story.Choices[i].FirstOrDefault();
            if (choice is null)
            {
                continue;
            }

            var success = BuildOutcome(choice.SuccessEffect, choice.SuccessEffectValue);
            var failed = BuildOutcome(choice.FailedEffect, choice.FailedEffectValue);
            var choiceResponse = new UmaEventChoiceResponse
            {
                OptionIndex = i + 1,
                OptionText = NormalizeOption(choice.Option),
                Success = success,
                Failed = failed
            };

            if (choiceResponse.OptionText is not null)
            {
                hasChoice = true;
            }

            if (failed is not null)
            {
                hasFailedBranch = true;
            }

            if (choice.SuccessEffectValue is not null || choice.FailedEffectValue is not null)
            {
                hasStructured = true;
            }
            else if (ContainsFixedText(success) || ContainsFixedText(failed))
            {
                hasFixed = true;
            }
            else if (ContainsMeaningfulText(success) || ContainsMeaningfulText(failed))
            {
                hasTextOnly = true;
            }

            if (ContainsKiremono(choice, success) || ContainsKiremono(choice, failed))
            {
                mayContainKiremono = true;
            }

            response.ChoiceGroups.Add(choiceResponse);
        }

        if (!hasChoice && response.ChoiceGroups.Count > 1)
        {
            hasChoice = true;
        }

        response.HasChoice = hasChoice;
        response.HasFailedBranch = hasFailedBranch;
        response.MayContainKiremono = mayContainKiremono;
        response.EffectMode = ResolveEffectMode(hasStructured, hasFixed, hasTextOnly);
        return response;
    }

    private static UmaEventOutcomeResponse? BuildOutcome(string text, EffectValue? effectValue)
    {
        var normalized = NormalizeNullable(text);
        var values = BuildValues(effectValue);
        var skillNames = effectValue?.SkillNames.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        var extras = effectValue?.Extras.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        var buffName = NormalizeNullable(effectValue?.BuffName ?? string.Empty);

        if (normalized is null && values is null && skillNames.Count == 0 && extras.Count == 0 && buffName is null)
        {
            return null;
        }

        return new UmaEventOutcomeResponse
        {
            Text = normalized,
            Values = values,
            SkillNames = skillNames,
            Extras = extras,
            BuffName = buffName
        };
    }

    private static UmaEventEffectValuesResponse? BuildValues(EffectValue? effectValue)
    {
        if (effectValue is null)
        {
            return null;
        }

        return new UmaEventEffectValuesResponse
        {
            Speed = ReadValue(effectValue.Values, 0),
            Stamina = ReadValue(effectValue.Values, 1),
            Power = ReadValue(effectValue.Values, 2),
            Guts = ReadValue(effectValue.Values, 3),
            Wisdom = ReadValue(effectValue.Values, 4),
            SkillPt = ReadValue(effectValue.Values, 5),
            HintLevel = ReadValue(effectValue.Values, 6),
            Vital = ReadValue(effectValue.Values, 7),
            Bond = ReadValue(effectValue.Values, 8),
            Motivation = ReadValue(effectValue.Values, 9)
        };
    }

    private static SnapshotBuildResult FinalizeSnapshot(
        Dictionary<int, CharacterAccumulator> characterMap,
        DateTimeOffset generatedAt,
        string sourceVersion,
        IReadOnlyCollection<StoryEvent> unclassified)
    {
        var generated = generatedAt.UtcDateTime.ToString("O");
        var details = characterMap.Values
            .Where(x => x.HasAnyEvents)
            .OrderBy(x => x.CharacterId)
            .Select(x => x.ToDetail(generated))
            .ToList();

        var catalog = new UmaEventCatalogResponse
        {
            Locale = "ja-JP",
            TextVariant = "female",
            SourceType = "derbyhub-db",
            SourceVersion = sourceVersion,
            GeneratedAt = generated
        };

        var characters = new Dictionary<int, UmaEventCharacterDetailResponse>();
        var variantCount = 0;
        var eventCount = 0;
        foreach (var detail in details)
        {
            characters[detail.CharacterId] = detail;
            catalog.Characters.Add(new UmaEventCatalogCharacterResponse
            {
                CharacterId = detail.CharacterId,
                NameJa = detail.NameJa,
                BaseVariantId = detail.BaseVariantId,
                BaseEventCount = detail.BaseEvents.Count,
                CharacterCommonEventCount = detail.CharacterCommonEvents.Count,
                Variants = [.. detail.Variants]
            });

            variantCount += detail.Variants.Count;
            eventCount += detail.BaseEvents.Count;
            eventCount += detail.CharacterCommonEvents.Count;
            eventCount += detail.OutfitVariants.Sum(x => x.ExclusiveEvents.Count);
        }

        catalog.CharacterCount = details.Count;
        catalog.VariantCount = variantCount;
        catalog.EventCount = eventCount;

        var manifest = new UmaEventManifestResponse
        {
            Locale = "ja-JP",
            TextVariant = "female",
            SourceType = "derbyhub-db",
            SourceVersion = sourceVersion,
            GeneratedAt = generated,
            CharacterCount = details.Count,
            VariantCount = variantCount,
            EventCount = eventCount
        };

        return new SnapshotBuildResult
        {
            Snapshot = new UmaEventSnapshotData
            {
                Manifest = manifest,
                Catalog = catalog,
                Characters = characters
            },
            CharacterCount = details.Count,
            VariantCount = variantCount,
            EventCount = eventCount,
            UnclassifiedEvents = unclassified.ToList()
        };
    }

    private static int ReadValue(int[] values, int index)
    {
        return index >= 0 && values.Length > index ? values[index] : 0;
    }

    private static string? NormalizeOption(string optionText)
    {
        var normalized = NormalizeNullable(optionText);
        return normalized is null || normalized == "系统事件" || normalized == "无选项" ? null : normalized;
    }

    private static string? NormalizeNullable(string text)
    {
        var normalized = text.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool ContainsFixedText(UmaEventOutcomeResponse? outcome)
    {
        return outcome?.Text == "固定效果";
    }

    private static bool ContainsMeaningfulText(UmaEventOutcomeResponse? outcome)
    {
        return outcome?.Text is not null && outcome.Text != "固定效果";
    }

    private static bool ContainsKiremono(Choice choice, UmaEventOutcomeResponse? outcome)
    {
        if (ContainsAlias(choice.SuccessEffect) || ContainsAlias(choice.FailedEffect))
        {
            return true;
        }

        if (outcome is null)
        {
            return false;
        }

        return ContainsAlias(outcome.Text)
            || ContainsAlias(outcome.BuffName)
            || outcome.Extras.Any(ContainsAlias);
    }

    private static bool ContainsAlias(string? text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && KiremonoAliases.Any(alias => text.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveEffectMode(bool hasStructured, bool hasFixed, bool hasTextOnly)
    {
        if (hasStructured)
        {
            return "structured";
        }

        if (hasFixed)
        {
            return "fixed";
        }

        return hasTextOnly ? "text_only" : "empty";
    }

    private sealed class CharacterAccumulator
    {
        private readonly Dictionary<int, UmaEventEventResponse> _baseEvents = [];
        private readonly Dictionary<int, UmaEventEventResponse> _commonEvents = [];
        private readonly Dictionary<int, VariantAccumulator> _variants = [];

        public CharacterAccumulator(int characterId, string nameJa, int baseVariantId)
        {
            CharacterId = characterId;
            NameJa = nameJa;
            BaseVariantId = baseVariantId;
            RegisterVariant(new VariantDefinition(characterId, baseVariantId, "base", nameJa));
        }

        public int CharacterId { get; }
        public string NameJa { get; }
        public int BaseVariantId { get; }
        public bool HasAnyEvents => _baseEvents.Count > 0 || _commonEvents.Count > 0 || _variants.Values.Any(x => x.HasExclusiveEvents);

        public void RegisterVariant(VariantDefinition definition)
        {
            _variants.TryAdd(definition.VariantId, new VariantAccumulator(definition.VariantId, definition.VariantType, definition.VariantNameJa));
        }

        public void AddBaseEvent(UmaEventEventResponse eventResponse)
        {
            _baseEvents.TryAdd(eventResponse.EventId, eventResponse);
        }

        public void AddCommonEvent(UmaEventEventResponse eventResponse)
        {
            _commonEvents.TryAdd(eventResponse.EventId, eventResponse);
        }

        public void AddOutfitExclusiveEvent(int variantId, UmaEventEventResponse eventResponse)
        {
            if (_variants.TryGetValue(variantId, out var variant))
            {
                variant.AddExclusiveEvent(eventResponse);
            }
        }

        public UmaEventCharacterDetailResponse ToDetail(string generatedAt)
        {
            var response = new UmaEventCharacterDetailResponse
            {
                CharacterId = CharacterId,
                NameJa = NameJa,
                BaseVariantId = BaseVariantId,
                GeneratedAt = generatedAt,
                BaseEvents = SortEvents(_baseEvents.Values),
                CharacterCommonEvents = SortEvents(_commonEvents.Values)
            };

            foreach (var variant in _variants.Values.OrderBy(x => x.VariantId))
            {
                response.Variants.Add(variant.ToSummary());
                if (variant.VariantType != "base")
                {
                    response.OutfitVariants.Add(variant.ToDetail());
                }
            }

            return response;
        }

        private static List<UmaEventEventResponse> SortEvents(IEnumerable<UmaEventEventResponse> events)
        {
            return events.OrderBy(x => x.EventId).ToList();
        }
    }

    private sealed class VariantAccumulator
    {
        private readonly Dictionary<int, UmaEventEventResponse> _exclusiveEvents = [];

        public VariantAccumulator(int variantId, string variantType, string variantNameJa)
        {
            VariantId = variantId;
            VariantType = variantType;
            VariantNameJa = variantNameJa;
        }

        public int VariantId { get; }
        public string VariantType { get; }
        public string VariantNameJa { get; }
        public bool HasExclusiveEvents => _exclusiveEvents.Count > 0;

        public void AddExclusiveEvent(UmaEventEventResponse eventResponse)
        {
            _exclusiveEvents.TryAdd(eventResponse.EventId, eventResponse);
        }

        public UmaEventVariantSummaryResponse ToSummary()
        {
            return new UmaEventVariantSummaryResponse
            {
                VariantId = VariantId,
                VariantType = VariantType,
                VariantNameJa = VariantNameJa,
                ExclusiveEventCount = _exclusiveEvents.Count
            };
        }

        public UmaEventVariantDetailResponse ToDetail()
        {
            return new UmaEventVariantDetailResponse
            {
                VariantId = VariantId,
                VariantType = VariantType,
                VariantNameJa = VariantNameJa,
                ExclusiveEventCount = _exclusiveEvents.Count,
                ExclusiveEvents = _exclusiveEvents.Values.OrderBy(x => x.EventId).ToList()
            };
        }
    }
}

public sealed class SnapshotBuildResult
{
    public UmaEventSnapshotData Snapshot { get; init; } = new();
    public int CharacterCount { get; init; }
    public int VariantCount { get; init; }
    public int EventCount { get; init; }
    public List<StoryEvent> UnclassifiedEvents { get; init; } = [];
}
