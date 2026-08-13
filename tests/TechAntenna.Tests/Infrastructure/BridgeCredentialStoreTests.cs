using Microsoft.Data.Sqlite;
using TechAntenna.Infrastructure.Bridge;

namespace TechAntenna.Tests.Infrastructure;

public class BridgeCredentialStoreTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    readonly string _directory = Path.Combine(
        Path.GetTempPath(), "tech-antenna-bridge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    (string? Credential, long Enabled) Read()
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = BridgeCredentialStore.PathIn(_directory),
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT credential, enabled FROM provider_settings WHERE provider = 'claude'";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt64(1));
    }

    [Fact]
    public void ブリッジが読む表にトークンを書く()
    {
        BridgeCredentialStore.Write(_directory, "token-1", Now);

        Assert.Equal(("token-1", 1L), Read());
    }

    [Fact]
    public void 入れ替えたトークンで上書きする()
    {
        BridgeCredentialStore.Write(_directory, "token-1", Now);
        BridgeCredentialStore.Write(_directory, "token-2", Now);

        Assert.Equal(("token-2", 1L), Read());
    }

    [Fact]
    public void 画面から消したら無効にする()
    {
        // 残したままだと、Anthropic API へ切り替えたつもりでブリッジが古いトークンで動き続ける
        BridgeCredentialStore.Write(_directory, "token-1", Now);
        BridgeCredentialStore.Write(_directory, null, Now);

        Assert.Equal((null, 0L), Read());
    }

    [Fact]
    public void WALにしない()
    {
        // ブリッジは読み取り専用でマウントして読むので、WAL だと -shm を作れず開けない
        BridgeCredentialStore.Write(_directory, "token-1", Now);

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = BridgeCredentialStore.PathIn(_directory),
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";

        Assert.Equal("delete", (command.ExecuteScalar() as string)?.ToLowerInvariant());
    }

    [Fact]
    public void 共有ディレクトリが未設定なら理由つきで投げる()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BridgeCredentialStore.Write("", "token-1", Now));

        Assert.Contains("StateDirectory", ex.Message);
    }
}
