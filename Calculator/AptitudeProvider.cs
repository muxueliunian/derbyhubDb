using Microsoft.Data.Sqlite;

namespace derbyhubDb.Calculator;

public sealed class AptitudeProvider
{
    public LocalAptitudeData Load(string masterMdb)
    {
        var result = new LocalAptitudeData();
        if (!File.Exists(masterMdb))
        {
            result.Error = $"找不到 master.mdb: {masterMdb}";
            return result;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = masterMdb,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            select card_id,
                   proper_ground_turf,
                   proper_ground_dirt,
                   proper_distance_short,
                   proper_distance_mile,
                   proper_distance_middle,
                   proper_distance_long,
                   proper_running_style_nige,
                   proper_running_style_senko,
                   proper_running_style_sashi,
                   proper_running_style_oikomi
            from card_rarity_data
            where rarity = (
                select min(inner_card.rarity)
                from card_rarity_data inner_card
                where inner_card.card_id = card_rarity_data.card_id
            )
            order by card_id
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var cardId = reader.GetInt32(0);
            var aptitude = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["turf"] = ToCalculatorValue(reader.GetInt32(1)),
                ["dirt"] = ToCalculatorValue(reader.GetInt32(2)),
                ["short"] = ToCalculatorValue(reader.GetInt32(3)),
                ["mile"] = ToCalculatorValue(reader.GetInt32(4)),
                ["middle"] = ToCalculatorValue(reader.GetInt32(5)),
                ["long"] = ToCalculatorValue(reader.GetInt32(6)),
                ["nige"] = ToCalculatorValue(reader.GetInt32(7)),
                ["senko"] = ToCalculatorValue(reader.GetInt32(8)),
                ["sashi"] = ToCalculatorValue(reader.GetInt32(9)),
                ["oikomi"] = ToCalculatorValue(reader.GetInt32(10))
            };

            if (CalculatorAptitudeKeys.IsComplete(aptitude))
            {
                result.AptitudeByCardId[cardId] = aptitude;
            }
        }

        return result;
    }

    private static int ToCalculatorValue(int masterValue)
    {
        return masterValue is >= 1 and <= 7 ? 8 - masterValue : 7;
    }
}

public sealed class LocalAptitudeData
{
    public Dictionary<int, Dictionary<string, int>> AptitudeByCardId { get; } = [];
    public string? Error { get; set; }
}
