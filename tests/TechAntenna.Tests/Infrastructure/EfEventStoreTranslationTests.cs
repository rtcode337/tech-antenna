using Microsoft.EntityFrameworkCore;
using TechAntenna.Infrastructure.Persistence;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// EF の LINQ が SQL に翻訳できるかを、**DB につながずに**確かめる
/// (<c>ToQueryString</c> は接続を開かない。テストが PostgreSQL を要らないのは今までどおり)。
///
/// **InMemory のストアで通っても、EF 版だけ実行時に落ちることがある。** 実際、主催者ごとの
/// 件数を record のコンストラクタで射影したまま並べ替えていて、画面がエラーになった
/// (「The LINQ expression could not be translated」)。翻訳できるかどうかは
/// 動かすまで分からないので、問い合わせの形が変わったらここで気づけるようにしておく。
/// </summary>
public class EfEventStoreTranslationTests
{
    static TechAntennaDbContext Context() =>
        new(new DbContextOptionsBuilder<TechAntennaDbContext>()
            // 接続はしないので、実在しないホストでよい
            .UseNpgsql("Host=localhost;Database=none")
            .Options);

    [Fact]
    public void 主催者ごとの件数が翻訳できる()
    {
        using var db = Context();

        // ストアが実際に使う問い合わせをそのまま掛ける(書き写すと、直したときにずれる)
        var sql = EfEventStore.OrganizerCountQuery(db).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        // 並べ替えまで SQL 側で行われていること(件数の多い順に出すのが画面の前提)
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 期間で切り出す問い合わせが翻訳できる()
    {
        using var db = Context();

        var sql = db.Events
            .Where(e => e.StartsAt >= DateTimeOffset.UnixEpoch && e.StartsAt < DateTimeOffset.UnixEpoch)
            .OrderBy(e => e.StartsAt)
            .Take(10)
            .ToQueryString();

        Assert.Contains("\"StartsAt\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void 既存イベントをURLで引き直す問い合わせが翻訳できる()
    {
        using var db = Context();
        // Url は値変換(Uri ↔ text)が掛かっている列。収集のたびに参加者数を取り込むため、
        // 既存の行をこの形で引き直している
        var urls = new List<Uri> { new("https://example.com/a") };

        var sql = db.Events.Where(e => urls.Contains(e.Url)).ToQueryString();

        Assert.Contains("\"Url\"", sql, StringComparison.Ordinal);
    }
}
