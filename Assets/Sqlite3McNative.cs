using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;

namespace derbyhubDb.Assets;

public sealed class Sqlite3McConnection : IDisposable
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteOpenReadOnly = 0x00000001;
    private const int SqliteOpenReadWrite = 0x00000002;
    private const string LibraryName = "sqlite3mc_x64";

    private static string? _dllPath;
    private static bool _resolverRegistered;

    private IntPtr _db;

    public static void ConfigureLibrary(string? dllPath)
    {
        if (!string.IsNullOrWhiteSpace(dllPath))
        {
            _dllPath = Path.GetFullPath(dllPath);
        }

        if (_resolverRegistered)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(Sqlite3McConnection).Assembly, ResolveDllImport);
        _resolverRegistered = true;
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(_dllPath) && File.Exists(_dllPath);

    public static string? ConfiguredPath => _dllPath;

    public Sqlite3McConnection(string path, byte[] key, int cipherIndex)
    {
        var rc = sqlite3_open_v2(path, out _db, SqliteOpenReadWrite, IntPtr.Zero);
        if (rc != SqliteOk || _db == IntPtr.Zero)
        {
            throw new InvalidOperationException($"sqlite3_open_v2 failed rc={rc} errmsg={GetErrorMessage()}");
        }

        rc = sqlite3mc_config(_db, "cipher", cipherIndex);
        _ = rc;

        rc = sqlite3_key(_db, key, key.Length);
        if (rc != SqliteOk)
        {
            throw new InvalidOperationException($"sqlite3_key failed rc={rc} errmsg={GetErrorMessage()}");
        }

        Exec("select name from sqlite_master limit 1");
    }

    public int Count(string sql)
    {
        var value = ScalarInt64(sql);
        return Convert.ToInt32(value);
    }

    public bool HasColumn(string tableName, string columnName)
    {
        var escapedTable = EscapeIdentifier(tableName);
        var expected = columnName;
        var found = false;
        ForEachRow($"pragma table_info({escapedTable})", columns =>
        {
            if (string.Equals(columns.GetString(1), expected, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
            }
        });
        return found;
    }

    public void ReadCandidates(
        string token,
        string datDir,
        LocalAssetLookupRequest request,
        bool hasEncryptionColumn,
        List<LocalAssetCandidate> candidates)
    {
        var escapedToken = EscapeSqlLike(token);
        var sql = hasEncryptionColumn
            ? $"select n,h,m,d,e from a where n like '%{escapedToken}%' escape '\\' order by n limit 20"
            : $"select n,h,m,d from a where n like '%{escapedToken}%' escape '\\' order by n limit 20";

        ForEachRow(sql, columns =>
        {
            var hash = columns.GetString(1);
            if (string.IsNullOrWhiteSpace(hash))
            {
                return;
            }

            var localPath = Path.Combine(datDir, hash[..Math.Min(2, hash.Length)], hash);
            candidates.Add(new LocalAssetCandidate
            {
                CharacterId = request.CharacterId,
                CardId = request.CardId,
                SearchToken = token,
                Name = columns.GetString(0) ?? string.Empty,
                Hash = hash,
                Type = columns.GetString(2) ?? string.Empty,
                Dependencies = columns.GetString(3),
                EncryptionKey = hasEncryptionColumn ? columns.GetInt64(4) : null,
                LocalPath = localPath,
                LocalFileExists = File.Exists(localPath)
            });
        });
    }

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            sqlite3_close(_db);
            _db = IntPtr.Zero;
        }
    }

    private long ScalarInt64(string sql)
    {
        long value = 0;
        var hasValue = false;
        ForEachRow(sql, columns =>
        {
            if (!hasValue)
            {
                value = columns.GetInt64(0) ?? 0;
                hasValue = true;
            }
        });

        return value;
    }

    private void Exec(string sql)
    {
        var rc = sqlite3_exec(_db, sql, IntPtr.Zero, IntPtr.Zero, out var err);
        if (rc == SqliteOk)
        {
            return;
        }

        var message = PtrToStringUtf8(err);
        if (err != IntPtr.Zero)
        {
            sqlite3_free(err);
        }

        throw new InvalidOperationException($"sqlite3_exec failed rc={rc} errmsg={message ?? GetErrorMessage()} sql={sql}");
    }

    private void ForEachRow(string sql, Action<Sqlite3McColumns> row)
    {
        var rc = sqlite3_prepare_v2(_db, sql, -1, out var stmt, IntPtr.Zero);
        if (rc != SqliteOk)
        {
            throw new InvalidOperationException($"sqlite3_prepare_v2 failed rc={rc} errmsg={GetErrorMessage()} sql={sql}");
        }

        try
        {
            while (true)
            {
                rc = sqlite3_step(stmt);
                if (rc == SqliteRow)
                {
                    row(new Sqlite3McColumns(stmt));
                    continue;
                }

                if (rc == SqliteDone)
                {
                    break;
                }

                throw new InvalidOperationException($"sqlite3_step failed rc={rc} errmsg={GetErrorMessage()} sql={sql}");
            }
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
    }

    private string GetErrorMessage()
    {
        return PtrToStringUtf8(sqlite3_errmsg(_db)) ?? "(null)";
    }

    private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        if (libraryName == LibraryName && IsConfigured)
        {
            return NativeLibrary.Load(_dllPath!);
        }

        return IntPtr.Zero;
    }

    private static string EscapeIdentifier(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeSqlLike(string value)
    {
        return value
            .Replace("'", "''", StringComparison.Ordinal)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static string? PtrToStringUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        var length = 0;
        while (Marshal.ReadByte(ptr, length) != 0)
        {
            length++;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        var buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);
        return Encoding.UTF8.GetString(buffer);
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPStr)] string filename,
        out IntPtr db,
        int flags,
        IntPtr vfs);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(
        IntPtr db,
        [MarshalAs(UnmanagedType.LPStr)] string sql,
        IntPtr callback,
        IntPtr arg,
        out IntPtr errMsg);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr ptr);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3mc_config(
        IntPtr db,
        [MarshalAs(UnmanagedType.LPStr)] string paramName,
        int newValue);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_key(IntPtr db, byte[] key, int keyLength);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(
        IntPtr db,
        [MarshalAs(UnmanagedType.LPStr)] string sql,
        int bytes,
        out IntPtr statement,
        IntPtr tail);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int column);

    private sealed class Sqlite3McColumns
    {
        private readonly IntPtr _statement;

        public Sqlite3McColumns(IntPtr statement)
        {
            _statement = statement;
        }

        public string? GetString(int column)
        {
            var pointer = sqlite3_column_text(_statement, column);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            var length = sqlite3_column_bytes(_statement, column);
            if (length <= 0)
            {
                return string.Empty;
            }

            var buffer = new byte[length];
            Marshal.Copy(pointer, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer);
        }

        public long? GetInt64(int column)
        {
            return sqlite3_column_int64(_statement, column);
        }
    }
}
