namespace derbyhubDb.Effects;

public sealed class CorrectionTables
{
    public Dictionary<string, string> EventNames { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> TriggerNames { get; init; } = new(StringComparer.Ordinal);

    public static CorrectionTables Load(string? correctionsDir)
    {
        var result = new CorrectionTables();
        if (string.IsNullOrWhiteSpace(correctionsDir))
        {
            return result;
        }

        LoadFile(Path.Combine(correctionsDir, "correctedEventNames.txt"), result.EventNames);
        LoadFile(Path.Combine(correctionsDir, "correctedTriggerNames.txt"), result.TriggerNames);

        result.EventNames.TryAdd("きんぐちゃんとがんばる！", "キングちゃんとがんばる！");
        result.EventNames.TryAdd("「いつもの」ください", "『いつもの』ください！");
        return result;
    }

    public string CorrectEventName(string value)
    {
        return EventNames.GetValueOrDefault(value, value);
    }

    public string CorrectTriggerName(string value)
    {
        return TriggerNames.GetValueOrDefault(value, value);
    }

    private static void LoadFile(string path, Dictionary<string, string> target)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split("【分隔符】", 2, StringSplitOptions.None);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                target[parts[0]] = parts[1];
            }
        }
    }
}
