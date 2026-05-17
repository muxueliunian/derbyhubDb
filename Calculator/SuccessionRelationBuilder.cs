using Microsoft.Data.Sqlite;

namespace derbyhubDb.Calculator;

public sealed class SuccessionRelationBuilder
{
    public LocalCompatibilityData Build(string masterMdb, IReadOnlyList<int> characterIds)
    {
        var result = new LocalCompatibilityData();
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

        var relationPoints = ReadRelationPoints(connection);
        var membersByRelation = ReadRelationMembers(connection);
        if (relationPoints.Count == 0 || membersByRelation.Count == 0)
        {
            result.Error = "master.mdb 中 succession_relation 或 succession_relation_member 为空";
            return result;
        }

        var characterSet = characterIds.ToHashSet();
        var relationTypesByCharacter = characterIds.ToDictionary(id => id, _ => new List<int>());
        foreach (var (relationType, members) in membersByRelation)
        {
            foreach (var characterId in members)
            {
                if (characterSet.Contains(characterId))
                {
                    relationTypesByCharacter[characterId].Add(relationType);
                }
            }
        }

        for (var i = 0; i < characterIds.Count; i++)
        {
            var characterA = characterIds[i];
            var membershipsA = relationTypesByCharacter[characterA];
            for (var j = i; j < characterIds.Count; j++)
            {
                var characterB = characterIds[j];
                var score = 0;
                foreach (var relationType in membershipsA)
                {
                    if (membersByRelation.TryGetValue(relationType, out var members)
                        && members.Contains(characterB)
                        && relationPoints.TryGetValue(relationType, out var point))
                    {
                        score += point;
                    }
                }

                if (score > 0)
                {
                    result.CompatibilityTable[$"{characterA}_{characterB}"] = score;
                }
            }
        }

        return result;
    }

    private static Dictionary<int, int> ReadRelationPoints(SqliteConnection connection)
    {
        var result = new Dictionary<int, int>();
        using var command = connection.CreateCommand();
        command.CommandText = "select relation_type, relation_point from succession_relation";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return result;
    }

    private static Dictionary<int, HashSet<int>> ReadRelationMembers(SqliteConnection connection)
    {
        var result = new Dictionary<int, HashSet<int>>();
        using var command = connection.CreateCommand();
        command.CommandText = "select relation_type, chara_id from succession_relation_member";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var relationType = reader.GetInt32(0);
            var characterId = reader.GetInt32(1);
            if (!result.TryGetValue(relationType, out var members))
            {
                members = [];
                result[relationType] = members;
            }

            members.Add(characterId);
        }

        return result;
    }
}

public sealed class LocalCompatibilityData
{
    public Dictionary<string, int> CompatibilityTable { get; } = [];
    public string? Error { get; set; }
}
