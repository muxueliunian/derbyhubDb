using Microsoft.Data.Sqlite;

namespace derbyhubDb.Assets;

public sealed class GameAssetProbeResult
{
    public string GameDataRoot { get; set; } = string.Empty;
    public string? MetaDbPath { get; set; }
    public bool MetaDbExists { get; set; }
    public bool MetaDbReadable { get; set; }
    public bool MetaDbEncrypted { get; set; }
    public int MetaEntryCount { get; set; }
    public string? Sqlite3McDllPath { get; set; }
    public bool Sqlite3McAvailable { get; set; }
    public string? MetaDbError { get; set; }
    public bool GameDataRootExists { get; set; }
    public bool PersistentExists { get; set; }
    public bool ManifestDirExists { get; set; }
    public bool DatDirExists { get; set; }
    public bool AssetBundleDirExists { get; set; }
    public bool MasterMdbExists { get; set; }
    public bool MasterHasDirectImageMapping { get; set; }
    public List<string> MasterTablesChecked { get; set; } = [];
    public List<string> UnityBundleSamples { get; set; } = [];
    public List<LocalAssetCandidate> LocalAssetCandidates { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public string Conclusion { get; set; } = string.Empty;
}

public sealed class LocalAssetCandidate
{
    public int CharacterId { get; set; }
    public int CardId { get; set; }
    public string SearchToken { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Dependencies { get; set; }
    public long? EncryptionKey { get; set; }
    public string? LocalPath { get; set; }
    public bool LocalFileExists { get; set; }
}

public sealed record LocalAssetLookupRequest(int CharacterId, int CardId);

public sealed class GameAssetProbe
{
    private static readonly string[] CandidateTables =
    [
        "card_data",
        "chara_data",
        "dress_data",
        "text_data"
    ];

    private static readonly string[] ImageColumnNeedles =
    [
        "asset",
        "bundle",
        "resource",
        "path",
        "icon",
        "thumb",
        "stand"
    ];

    public GameAssetProbeResult Probe(
        string gameDataRoot,
        string masterMdb,
        string? metaDb,
        string? sqlite3McDll,
        IReadOnlyCollection<LocalAssetLookupRequest>? lookupRequests = null)
    {
        var result = new GameAssetProbeResult
        {
            GameDataRoot = Path.GetFullPath(gameDataRoot)
        };

        var persistent = Path.Combine(gameDataRoot, "Persistent");
        var manifest = Path.Combine(persistent, "manifest");
        var dat = Path.Combine(persistent, "dat");
        var assetbundle = Path.Combine(persistent, "assetbundle");

        result.GameDataRootExists = Directory.Exists(gameDataRoot);
        result.PersistentExists = Directory.Exists(persistent);
        result.ManifestDirExists = Directory.Exists(manifest);
        result.DatDirExists = Directory.Exists(dat);
        result.AssetBundleDirExists = Directory.Exists(assetbundle);
        result.MasterMdbExists = File.Exists(masterMdb);

        if (!result.ManifestDirExists)
        {
            result.Notes.Add("Persistent\\manifest 不存在，无法从独立 manifest 目录解析 bundle 名称。");
        }

        if (!result.AssetBundleDirExists)
        {
            result.Notes.Add("Persistent\\assetbundle 不存在，资源位于 dat 哈希目录时需要额外索引才能定位。");
        }

        if (result.MasterMdbExists)
        {
            ProbeMaster(result, masterMdb);
        }

        if (result.DatDirExists)
        {
            ProbeUnityBundles(result, dat);
        }

        ProbeMetaDb(result, gameDataRoot, metaDb, sqlite3McDll, lookupRequests ?? []);

        result.Conclusion = BuildConclusion(result);
        return result;
    }

    private static void ProbeMaster(GameAssetProbeResult result, string masterMdb)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = masterMdb,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        foreach (var table in CandidateTables)
        {
            result.MasterTablesChecked.Add(table);
            using var command = connection.CreateCommand();
            command.CommandText = $"pragma table_info({table})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var columnName = reader.GetString(1);
                if (ImageColumnNeedles.Any(x => columnName.Contains(x, StringComparison.OrdinalIgnoreCase)))
                {
                    result.MasterHasDirectImageMapping = true;
                    result.Notes.Add($"master 表 {table}.{columnName} 可能包含图像相关信息。");
                }
            }
        }

        if (!result.MasterHasDirectImageMapping)
        {
            result.Notes.Add("card_data/chara_data/dress_data/text_data 未发现直接的头像 asset path、bundle name 或 resource key 字段。");
        }
    }

    private static void ProbeUnityBundles(GameAssetProbeResult result, string datDir)
    {
        foreach (var file in Directory.EnumerateFiles(datDir, "*", SearchOption.AllDirectories).Take(300))
        {
            var header = new byte[7];
            using var stream = File.OpenRead(file);
            var read = stream.Read(header);
            if (read < 7)
            {
                continue;
            }

            if (header.AsSpan().SequenceEqual("UnityFS"u8))
            {
                result.UnityBundleSamples.Add(file);
                if (result.UnityBundleSamples.Count >= 5)
                {
                    break;
                }
            }
        }

        if (result.UnityBundleSamples.Count > 0)
        {
            result.Notes.Add("dat 目录中检测到 UnityFS AssetBundle。");
        }
    }

    private static void ProbeMetaDb(
        GameAssetProbeResult result,
        string gameDataRoot,
        string? explicitMetaDb,
        string? sqlite3McDll,
        IReadOnlyCollection<LocalAssetLookupRequest> lookupRequests)
    {
        var persistent = Path.Combine(gameDataRoot, "Persistent");
        var candidatePaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitMetaDb))
        {
            candidatePaths.Add(explicitMetaDb);
        }

        candidatePaths.Add(Path.Combine(persistent, "meta_umaviewer"));
        candidatePaths.Add(Path.Combine(gameDataRoot, "meta_umaviewer"));
        candidatePaths.Add(Path.Combine(persistent, "meta"));

        foreach (var candidate in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(candidate);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            result.MetaDbPath = fullPath;
            result.MetaDbExists = true;
            var datDir = Path.Combine(persistent, "dat");
            if (TryReadMetaDb(result, fullPath, datDir, lookupRequests))
            {
                return;
            }

            if (TryReadEncryptedMetaDb(result, fullPath, datDir, sqlite3McDll, lookupRequests))
            {
                return;
            }
        }

        if (!result.MetaDbExists)
        {
            result.Notes.Add("未找到普通 sqlite/UmaViewer 形式的 meta 数据库；可用 --meta-db 指向 meta_umaviewer 后再探测。");
        }
        else if (!result.MetaDbReadable && !string.IsNullOrWhiteSpace(result.MetaDbError))
        {
            result.Notes.Add($"meta 数据库不可直接读取: {result.MetaDbError}");
        }
    }

    private static bool TryReadEncryptedMetaDb(
        GameAssetProbeResult result,
        string metaDbPath,
        string datDir,
        string? sqlite3McDll,
        IReadOnlyCollection<LocalAssetLookupRequest> lookupRequests)
    {
        var dllPath = ResolveSqlite3McDll(sqlite3McDll);
        result.Sqlite3McDllPath = dllPath;
        result.Sqlite3McAvailable = !string.IsNullOrWhiteSpace(dllPath) && File.Exists(dllPath);
        if (!result.Sqlite3McAvailable)
        {
            result.MetaDbError = $"{result.MetaDbError}; 未配置 sqlite3mc_x64.dll，无法按 UmaViewer 方法读取加密 meta";
            return false;
        }

        try
        {
            Sqlite3McConnection.ConfigureLibrary(dllPath);
            using var connection = new Sqlite3McConnection(metaDbPath, UmaViewerMetaCrypto.BuildJapaneseMetaKey(), cipherIndex: 3);
            result.MetaDbReadable = true;
            result.MetaDbEncrypted = true;
            result.MetaDbError = null;
            result.MetaEntryCount = connection.Count("select count(*) from a");
            result.Notes.Add($"已通过 sqlite3mc 读取加密 meta 数据库: {metaDbPath}");

            var hasEncryptionColumn = connection.HasColumn("a", "e");
            foreach (var request in lookupRequests)
            {
                ReadEncryptedCandidates(connection, datDir, request, hasEncryptionColumn, result.LocalAssetCandidates);
            }

            if (lookupRequests.Count > 0)
            {
                result.Notes.Add($"加密 meta 候选资源命中数: {result.LocalAssetCandidates.Count}");
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            result.MetaDbReadable = false;
            result.MetaDbEncrypted = true;
            result.MetaDbError = $"sqlite3mc 读取失败: {ex.Message}";
            return false;
        }
    }

    private static void ReadEncryptedCandidates(
        Sqlite3McConnection connection,
        string datDir,
        LocalAssetLookupRequest request,
        bool hasEncryptionColumn,
        List<LocalAssetCandidate> candidates)
    {
        foreach (var token in BuildSearchTokens(request))
        {
            var before = candidates.Count;
            connection.ReadCandidates(token, datDir, request, hasEncryptionColumn, candidates);
            if (candidates.Count > before)
            {
                break;
            }
        }
    }

    private static string? ResolveSqlite3McDll(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var env = Environment.GetEnvironmentVariable("SQLITE3MC_DLL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }

        var local = Path.Combine(Environment.CurrentDirectory, "tools", "sqlite3mc_x64.dll");
        if (File.Exists(local))
        {
            return local;
        }

        return null;
    }

    private static bool TryReadMetaDb(
        GameAssetProbeResult result,
        string metaDbPath,
        string datDir,
        IReadOnlyCollection<LocalAssetLookupRequest> lookupRequests)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = metaDbPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            if (!TableExists(connection, "a"))
            {
                result.MetaDbError = "未找到表 a";
                return false;
            }

            var columns = ReadColumns(connection, "a");
            if (!columns.Contains("n") || !columns.Contains("h") || !columns.Contains("m"))
            {
                result.MetaDbError = "表 a 缺少 n/h/m 字段";
                return false;
            }

            result.MetaDbReadable = true;
            result.MetaDbError = null;
            result.MetaEntryCount = CountMetaEntries(connection);
            result.Notes.Add($"已读取 meta 数据库: {metaDbPath}");

            foreach (var request in lookupRequests)
            {
                ReadCandidates(connection, columns, datDir, request, result.LocalAssetCandidates);
            }

            if (lookupRequests.Count > 0)
            {
                result.Notes.Add($"meta 候选资源命中数: {result.LocalAssetCandidates.Count}");
            }

            return true;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException)
        {
            result.MetaDbReadable = false;
            result.MetaDbError = ex.Message;
            return false;
        }
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select 1 from sqlite_master where type = 'table' and name = $name limit 1";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() is not null;
    }

    private static HashSet<string> ReadColumns(SqliteConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"pragma table_info({tableName})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static int CountMetaEntries(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from a";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void ReadCandidates(
        SqliteConnection connection,
        IReadOnlySet<string> columns,
        string datDir,
        LocalAssetLookupRequest request,
        List<LocalAssetCandidate> candidates)
    {
        foreach (var token in BuildSearchTokens(request))
        {
            var before = candidates.Count;
            var selectEncryption = columns.Contains("e");
            using var command = connection.CreateCommand();
            command.CommandText = selectEncryption
                ? "select n,h,m,d,e from a where n like $needle escape '\\' order by n limit 20"
                : "select n,h,m,d from a where n like $needle escape '\\' order by n limit 20";
            command.Parameters.AddWithValue("$needle", $"%{EscapeLike(token)}%");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var hash = reader.GetString(1);
                if (string.IsNullOrWhiteSpace(hash))
                {
                    continue;
                }

                var localPath = Path.Combine(datDir, hash[..Math.Min(2, hash.Length)], hash);
                candidates.Add(new LocalAssetCandidate
                {
                    CharacterId = request.CharacterId,
                    CardId = request.CardId,
                    SearchToken = token,
                    Name = name,
                    Hash = hash,
                    Type = reader.GetString(2),
                    Dependencies = reader.IsDBNull(3) ? null : reader.GetString(3),
                    EncryptionKey = selectEncryption && !reader.IsDBNull(4) ? reader.GetInt64(4) : null,
                    LocalPath = localPath,
                    LocalFileExists = File.Exists(localPath)
                });
            }

            if (candidates.Count > before)
            {
                break;
            }
        }
    }

    private static IEnumerable<string> BuildSearchTokens(LocalAssetLookupRequest request)
    {
        var charId = request.CharacterId.ToString();
        var cardId = request.CardId.ToString();
        yield return $"chara_stand_{charId}_{cardId}";
        yield return $"card_stand_{cardId}";
        yield return $"support_card_s_{cardId}";
        yield return $"support_card_l_{cardId}";
        yield return $"gacha_chara_{cardId}";
        yield return $"chara_card_{cardId}";
        yield return $"chara_card_{charId}_{cardId}";
        yield return $"icon_{cardId}";
        yield return $"dress_{cardId}";
        yield return $"card_{cardId}";
        yield return cardId;
        yield return $"chr_icon_{charId}";
        yield return $"chr{charId}";
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static string BuildConclusion(GameAssetProbeResult result)
    {
        if (result.MetaDbReadable && result.LocalAssetCandidates.Count > 0)
        {
            return "已能通过 UmaViewer/sqlite3mc 方法读取加密 meta，并把候选资源名映射到 dat 哈希文件；chara_stand 的 UnityFS/.resS DXT5 头像可本地导出。";
        }

        if (result.MetaDbExists && !result.MetaDbReadable)
        {
            return "检测到 meta 文件，但它不是普通 sqlite/UmaViewer meta，暂不能从中建立 asset name -> dat 哈希映射。";
        }

        if (!result.DatDirExists)
        {
            return "未找到本地 dat 资源目录，无法本地提取头像。";
        }

        if (result.MasterHasDirectImageMapping)
        {
            return "master 中存在疑似图像字段，但仍需要后续实现 bundle 定位与 Texture2D 解包。";
        }

        if (result.UnityBundleSamples.Count > 0)
        {
            return ".NET 已确认本地资源为 UnityFS AssetBundle，但当前缺少可验证的 asset key -> 哈希文件映射和 Texture2D 解码器；第一阶段保留接口，使用现有图片/GameTora fallback。";
        }

        return "未能确认可直接提取的本地头像资源，第一阶段使用现有图片/GameTora fallback。";
    }
}
