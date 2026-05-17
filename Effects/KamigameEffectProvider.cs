using System.Text.Json;
using derbyhubDb.MasterDb;

namespace derbyhubDb.Effects;

public sealed class KamigameEffectProvider
{
    public async Task<EffectLoadResult> LoadAsync(string? url, MasterData master, CorrectionTables corrections)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new EffectLoadResult { Warning = "未配置 Kamigame URL，选项收益为空。" };
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var payload = await http.GetStringAsync(url);
            payload = payload
                .Replace("\\r\\n", "[Linebreak]", StringComparison.Ordinal)
                .Replace("\\n", "[Linebreak]", StringComparison.Ordinal)
                .Replace("[Linebreak]\"", "\"", StringComparison.Ordinal)
                .Replace("<br>", "", StringComparison.Ordinal);

            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new EffectLoadResult { Warning = "Kamigame JSON 不是数组，选项收益为空。" };
            }

            var result = new Dictionary<long, List<List<Choice>>>();
            var unmatched = new List<string>();
            var rows = document.RootElement.EnumerateArray().Skip(1).ToList();

            foreach (var row in rows)
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 7)
                {
                    continue;
                }

                var rawEventName = GetCell(row, 0);
                var category = GetCell(row, 1);
                var rawTriggerName = GetCell(row, 2);
                if (string.IsNullOrWhiteSpace(rawEventName) || string.IsNullOrWhiteSpace(rawTriggerName))
                {
                    continue;
                }

                if (category == "メインシナリオ" || category == "サポートカード")
                {
                    continue;
                }

                var eventName = corrections.CorrectEventName(rawEventName);
                var triggerName = corrections.CorrectTriggerName(rawTriggerName);
                var choices = BuildChoices(GetCell(row, 4), GetCell(row, 5), GetCell(row, 6));
                var candidates = FindStoryCandidates(master, eventName, triggerName).ToList();
                if (candidates.Count == 0)
                {
                    unmatched.Add($"{rawEventName} / {rawTriggerName}");
                    continue;
                }

                foreach (var story in candidates)
                {
                    result.TryAdd(story.StoryId, choices);
                    if (story.ShortStoryId != 0)
                    {
                        result.TryAdd(story.ShortStoryId, choices);
                    }
                }
            }

            return new EffectLoadResult
            {
                EffectsByStoryId = result,
                KamigameRowCount = rows.Count,
                MatchedEventCount = result.Count,
                UnmatchedEventCount = unmatched.Distinct(StringComparer.Ordinal).Count(),
                UnmatchedEvents = unmatched.Distinct(StringComparer.Ordinal).Order().Take(200).ToList()
            };
        }
        catch (Exception ex)
        {
            return new EffectLoadResult { Warning = $"读取 Kamigame 失败，选项收益为空: {ex.Message}" };
        }
    }

    private static IEnumerable<SingleModeStoryData> FindStoryCandidates(MasterData master, string eventName, string triggerName)
    {
        var exact = master.Stories.Where(x => x.Name == eventName).ToList();
        if (exact.Count == 0)
        {
            return [];
        }

        if (!master.NameToId.TryGetValue(triggerName, out var id))
        {
            return exact.Count == 1 ? exact : [];
        }

        var charaId = id.ToString().Length > 4
            ? long.Parse(id.ToString()[..4])
            : id;

        var scoped = exact
            .Where(x => x.CardCharaId == charaId || x.CardId == id)
            .ToList();

        return scoped.Count > 0 ? scoped : exact.Count == 1 ? exact : [];
    }

    private static List<List<Choice>> BuildChoices(string optionsText, string successText, string failureText)
    {
        var options = SplitLines(optionsText);
        var successes = SplitLines(successText);
        var failures = SplitLines(failureText);
        var result = new List<List<Choice>>();
        for (var i = 0; i < options.Length; i++)
        {
            var success = i < successes.Length ? NormalizeRaceEffect(successes[i]) : string.Empty;
            var failed = i < failures.Length ? NormalizeRaceEffect(failures[i]) : string.Empty;
            failed = failed == "-" ? string.Empty : failed;

            success = EffectParser.NormalizeEffectText(success);
            failed = EffectParser.NormalizeEffectText(failed);
            result.Add(
            [
                new Choice
                {
                    Option = options[i],
                    SuccessEffect = success,
                    FailedEffect = failed,
                    SuccessEffectValue = EffectParser.Parse(success),
                    FailedEffectValue = EffectParser.Parse(failed)
                }
            ]);
        }

        return result;
    }

    private static string[] SplitLines(string value)
    {
        return value.Split("[Linebreak]", StringSplitOptions.None);
    }

    private static string NormalizeRaceEffect(string value)
    {
        if (!value.Contains("【G1】", StringComparison.Ordinal))
        {
            return value;
        }

        return value.Split('、', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(x => !x.StartsWith("【", StringComparison.Ordinal))
            ?? value;
    }

    private static string GetCell(JsonElement row, int index)
    {
        if (row.GetArrayLength() <= index)
        {
            return string.Empty;
        }

        return row[index].ValueKind switch
        {
            JsonValueKind.String => row[index].GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            _ => row[index].ToString()
        };
    }
}
