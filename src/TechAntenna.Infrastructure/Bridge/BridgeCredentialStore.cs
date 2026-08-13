using Microsoft.Data.Sqlite;

namespace TechAntenna.Infrastructure.Bridge;

/// <summary>
/// CLI ブリッジが読む認証情報を、共有ディレクトリの SQLite に書く。
///
/// **ブリッジに口を開けない**ための形。ブリッジは相手ごとの認証情報を 1 つの表
/// (<c>provider_settings</c>)から読む前提で書かれていて、問い合わせを差し替える口は無い。
/// こちらが表を書き、ブリッジは**読み取り専用でマウント**して読む。
/// **読むのは要求のたび**なので、画面でトークンを入れ替えてもブリッジの再起動は要らない。
///
/// 表の形はブリッジ(chiezo リポジトリの <c>bridge/cli_bridge.py</c>)との約束で、
/// **列を減らさないこと** —— ブリッジは credential しか読まないが、同じ形のファイルを
/// chiezo 本体が開くこともある。
/// </summary>
public static class BridgeCredentialStore
{
    /// <summary>共有ディレクトリに置くファイル名(ブリッジ側の既定と揃える)。</summary>
    public const string SettingsFile = "settings.db";

    /// <summary>ブリッジ側で Claude Code を指す名前(<c>CHIEZO_BRIDGE_CLI</c> と同じ値)。</summary>
    const string ProviderClaude = "claude";

    const string Schema = """
        CREATE TABLE IF NOT EXISTS provider_settings (
            provider    TEXT PRIMARY KEY,
            enabled     INTEGER NOT NULL DEFAULT 0,
            credential  TEXT,
            model       TEXT,
            verified_at TEXT,
            updated_at  TEXT NOT NULL
        );
        """;

    public static string PathIn(string stateDirectory) => Path.Combine(stateDirectory, SettingsFile);

    /// <summary>
    /// トークンをブリッジが読める形に書く。<paramref name="token"/> が null なら
    /// 無効化する(画面から消したときに、古いトークンでブリッジが動き続けないように)。
    ///
    /// 呼ぶのは「トークンの状態が変わったとき」だけでよい(ブリッジは毎回読み直す)。
    /// </summary>
    public static void Write(string stateDirectory, string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(stateDirectory))
        {
            throw new InvalidOperationException(
                "CLI ブリッジと共有するディレクトリ(ClaudeCode:StateDirectory)が未設定です。");
        }

        Directory.CreateDirectory(stateDirectory);

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = PathIn(stateDirectory) }.ToString());
        connection.Open();

        // **WAL にしない。** ブリッジはこのファイルを読み取り専用でマウントして読むが、
        // WAL の読み手は -shm への書き込みを要求するので `unable to open database file` になる。
        // journal_mode はファイルに焼き付く属性なので、書かないだけでは戻らない(毎回指定する)
        Execute(connection, "PRAGMA journal_mode=DELETE;");
        Execute(connection, Schema);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO provider_settings (provider, enabled, credential, updated_at)
            VALUES ($provider, $enabled, $credential, $updated_at)
            ON CONFLICT(provider) DO UPDATE SET
                credential=excluded.credential,
                enabled=excluded.enabled,
                updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$provider", ProviderClaude);
        command.Parameters.AddWithValue("$enabled", token is null ? 0 : 1);
        command.Parameters.AddWithValue("$credential", (object?)token ?? DBNull.Value);
        // 機械が読む値なので UTC のまま(人に見せる場所ではない)
        command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("o"));
        command.ExecuteNonQuery();
    }

    static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
