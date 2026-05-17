using Microsoft.Data.Sqlite;

namespace derbyhubDb.MasterDb;

public static class MasterDbReader
{
    public static MasterData Read(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var textData = ReadTextData(connection);
        var supportCards = ReadSupportCards(connection);
        var storyData = ReadSingleModeStoryData(connection, textData);

        var baseNames = textData
            .Where(x => x.Id == 170 && x.Index >= 1000 && x.Index < 9000)
            .GroupBy(x => x.Index)
            .ToDictionary(x => x.Key, x => x.First().Text);

        var umaNames = textData
            .Where(x => x.Id == 5)
            .Select(x => new UmaName(x.Index, x.Text, ParseCharaId(x.Index)))
            .Where(x => baseNames.ContainsKey(x.CharaId))
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());

        var supportCardNames = supportCards
            .Select(x => new
            {
                x.Id,
                Name = textData.FirstOrDefault(t => t.Category == 76 && t.Index == x.Id)?.Text
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Name!);

        var nameToId = textData
            .Where(x => x.Index != 9100101 && x.Index != 9101101)
            .Where(x => (x.Id == 4 && x.Category == 4) || (x.Id == 6 && x.Category == 6) || (x.Id == 75 && x.Category == 75))
            .GroupBy(x => x.Text)
            .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.Ordinal);
        nameToId.TryAdd("系统", 1000);

        return new MasterData
        {
            TextData = textData,
            SupportCards = supportCards,
            Stories = storyData,
            BaseNames = baseNames,
            UmaNames = umaNames,
            SupportCardNames = supportCardNames,
            NameToId = nameToId
        };
    }

    private static List<TextData> ReadTextData(SqliteConnection connection)
    {
        var result = new List<TextData>();
        using var command = connection.CreateCommand();
        command.CommandText = "select category, id, [index], text from text_data";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TextData(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetString(3)));
        }

        return result;
    }

    private static List<SupportCardData> ReadSupportCards(SqliteConnection connection)
    {
        var result = new List<SupportCardData>();
        using var command = connection.CreateCommand();
        command.CommandText = "select id, chara_id, rarity, command_id from support_card_data";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SupportCardData(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return result;
    }

    private static List<SingleModeStoryData> ReadSingleModeStoryData(SqliteConnection connection, IReadOnlyCollection<TextData> textData)
    {
        var titleByStoryId = textData
            .Where(x => x.Id == 181 && x.Category == 181)
            .GroupBy(x => x.Index)
            .ToDictionary(x => x.Key, x => x.First().Text);

        var result = new List<SingleModeStoryData>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            select id, story_id, short_story_id, card_chara_id, card_id,
                   support_chara_id, support_card_id, gallery_main_scenario
            from single_mode_story_data
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var storyId = reader.GetInt64(1);
            result.Add(new SingleModeStoryData(
                reader.GetInt64(0),
                storyId,
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                titleByStoryId.GetValueOrDefault(storyId, "成長のヒント")));
        }

        return result;
    }

    private static long ParseCharaId(long cardId)
    {
        var raw = cardId.ToString();
        var slice = raw[0] == '9' && raw.Length >= 5 ? raw[1..5] : raw[..Math.Min(4, raw.Length)];
        return long.TryParse(slice, out var charaId) ? charaId : 0;
    }
}
