namespace derbyhubDb.UmaEvents;

public static class VariantIdentityResolver
{
    private const int BaseAwakeningLevel = 5;
    private const int AwakeningSevenLevel = 7;

    public static VariantIdentity Resolve(
        int characterId,
        int baseVariantId,
        int variantId,
        string variantType,
        IReadOnlyDictionary<long, long> defaultCardIds,
        VariantIdentityDiagnostics? diagnostics = null)
    {
        var eventVariantId = variantId;
        if (IsBaseVariant(characterId, baseVariantId, variantId, variantType))
        {
            if (defaultCardIds.TryGetValue(characterId, out var defaultCardId))
            {
                var cardId = checked((int)defaultCardId);
                return new VariantIdentity(
                    eventVariantId,
                    cardId,
                    cardId,
                    cardId,
                    "base",
                    BaseAwakeningLevel);
            }

            diagnostics?.Blocks.Add($"{characterId}/{variantId}: base variant 无法映射默认真实 card_id");
            return new VariantIdentity(eventVariantId, null, null, null, "base", BaseAwakeningLevel);
        }

        if (TryNormalizeAwakeningSeven(variantId, out var normalizedCardId))
        {
            return new VariantIdentity(
                eventVariantId,
                normalizedCardId,
                normalizedCardId,
                null,
                "awakening7",
                AwakeningSevenLevel);
        }

        if (variantId > 0)
        {
            return new VariantIdentity(
                eventVariantId,
                variantId,
                variantId,
                variantId,
                "card",
                BaseAwakeningLevel);
        }

        diagnostics?.Blocks.Add($"{characterId}/{variantId}: 无法解析 avatarCardId，已保留 null");
        return new VariantIdentity(eventVariantId, null, null, null, "unknown", null);
    }

    public static bool IsBaseVariant(
        int characterId,
        int baseVariantId,
        int variantId,
        string variantType)
    {
        return variantType.Equals("base", StringComparison.OrdinalIgnoreCase)
            || variantId == baseVariantId
            || variantId == characterId * 100;
    }

    public static int? ResolveAvatarCardId(
        int characterId,
        int baseVariantId,
        UmaEventVariantSummaryResponse variant)
    {
        if (variant.HasIdentityFields)
        {
            return variant.AvatarCardId;
        }

        if (IsBaseVariant(characterId, baseVariantId, variant.VariantId, variant.VariantType))
        {
            return null;
        }

        return TryNormalizeAwakeningSeven(variant.VariantId, out var cardId)
            ? cardId
            : variant.VariantId;
    }

    public static int? ResolveSearchCardId(
        int characterId,
        int baseVariantId,
        UmaEventVariantSummaryResponse variant)
    {
        if (variant.HasIdentityFields)
        {
            return variant.SearchCardId;
        }

        if (IsBaseVariant(characterId, baseVariantId, variant.VariantId, variant.VariantType))
        {
            return null;
        }

        return TryNormalizeAwakeningSeven(variant.VariantId, out var cardId)
            ? cardId
            : variant.VariantId;
    }

    private static bool TryNormalizeAwakeningSeven(int variantId, out int cardId)
    {
        var text = variantId.ToString();
        if (text.Length == 7 && text[0] == '9' && int.TryParse(text[1..], out cardId))
        {
            return true;
        }

        cardId = 0;
        return false;
    }
}

public sealed record VariantIdentity(
    int EventVariantId,
    int? CardId,
    int? AvatarCardId,
    int? SearchCardId,
    string VariantKind,
    int? AwakeningLevel);

public sealed class VariantIdentityDiagnostics
{
    public List<string> Warnings { get; } = [];
    public List<string> Blocks { get; } = [];
}
