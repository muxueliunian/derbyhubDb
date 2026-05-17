using System.Text.RegularExpressions;

namespace derbyhubDb.Effects;

public static partial class EffectParser
{
    private static readonly string[] EffectTextToId =
    [
        "スピード",
        "スタミナ",
        "パワー",
        "根性",
        "賢さ",
        "スキルPt",
        "ヒント",
        "体力",
        "絆",
        "やる気",
        "全ステータス",
        "獲得",
        "ランダムな",
        "直前のトレーニング",
        "全能力",
        "ランダムで",
        "全パフォーマンス",
        "解消",
        "進行イベント打ち切り",
        "イベント進行打ち切り",
        "トレーニングに現れるようになる",
        "トレーニングが制限",
        "トレーニング制限",
        "レース制限",
        "すべての競技",
        "固有スキル",
        "ファン数",
        "適性Pt",
        "新メンバー加入",
        "目標",
        "チームメンバー",
        "-"
    ];

    public static EffectValue? Parse(string effect)
    {
        if (string.IsNullOrWhiteSpace(effect))
        {
            return null;
        }

        var ret = new EffectValue();
        foreach (var rawPart in effect.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Replace("−", "-", StringComparison.Ordinal);
            var effectId = Array.FindIndex(EffectTextToId, part.Contains);
            var effectValue = 0;
            var valueMatch = SignedNumberRegex().Match(part);
            if (valueMatch.Success)
            {
                int.TryParse(valueMatch.Value, out effectValue);
            }

            if (effectId < 0)
            {
                ret.Extras.Add(part);
                continue;
            }

            if (effectId < 10)
            {
                ret.Values[effectId] = effectValue;
                if (part.Contains("やる気↑", StringComparison.Ordinal))
                {
                    ret.Values[9] = 1;
                }

                if (effectId == 6)
                {
                    var skillMatch = SkillNameRegex().Match(part);
                    if (skillMatch.Success)
                    {
                        ret.SkillNames.Add(skillMatch.Groups[1].Value);
                    }
                }
                else if (effectId == 7 && part.Contains("全回復", StringComparison.Ordinal))
                {
                    ret.Values[7] = 120;
                }
            }
            else
            {
                ParseSpecial(ret, effectId, effectValue, part);
            }
        }

        return ret;
    }

    public static string NormalizeEffectText(string effect)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["体力−"] = "体力-",
            ["全ての野菜+3"] = "全ての野菜+40",
            ["切れ物"] = "切れ者",
            ["のスキルLv"] = "のヒントLv",
            ["パワ+"] = "パワー+",
            ["スキル+"] = "スキルPt+",
            ["スイルPt"] = "スキルPt",
            ["スキルpt"] = "スキルPt",
            ["進行イベント終了"] = "進行イベント打ち切り"
        };

        var result = effect;
        foreach (var item in replacements)
        {
            result = result.Replace(item.Key, item.Value, StringComparison.Ordinal);
        }

        return result;
    }

    private static void ParseSpecial(EffectValue ret, int effectId, int effectValue, string part)
    {
        switch (effectId)
        {
            case 10:
            case 14:
            case 16:
                for (var i = 0; i < 5; i++)
                {
                    ret.Values[i] = effectValue;
                }
                break;
            case 6:
                var skillMatch = SkillNameRegex().Match(part);
                if (skillMatch.Success)
                {
                    ret.SkillNames.Add(skillMatch.Groups[1].Value);
                }
                ret.Values[6] = effectValue;
                break;
            case 25:
                ret.BuffName = "固有スキル";
                ret.Values[6] = effectValue;
                break;
            default:
                ret.Extras.Add(part);
                break;
        }
    }

    [GeneratedRegex("[+-]\\d+")]
    private static partial Regex SignedNumberRegex();

    [GeneratedRegex("「(.+?)」")]
    private static partial Regex SkillNameRegex();
}
