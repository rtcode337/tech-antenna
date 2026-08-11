using Microsoft.EntityFrameworkCore;
using TechAntenna.Core;
using TechAntenna.Core.Models;
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

    /// <summary>
    /// **Npgsql は timestamptz に時差 0 以外の DateTimeOffset を書けない**
    /// (Cannot write DateTimeOffset with Offset=09:00:00 ...)。収集元は `+09:00` のまま
    /// 返してくるし、カレンダーの月の境界も日本時間で決まるので、DbContext の値変換で
    /// 保存・問い合わせの直前に UTC へそろえている。**外すと、その列を使う画面と
    /// 収集がまとめて実行時に落ちる。**
    /// </summary>
    [Theory]
    [InlineData(typeof(TechEvent), nameof(TechEvent.StartsAt))]
    [InlineData(typeof(TechEvent), nameof(TechEvent.EndsAt))]
    [InlineData(typeof(TechEvent), nameof(TechEvent.CollectedAt))]
    [InlineData(typeof(Article), nameof(Article.PublishedAt))]
    [InlineData(typeof(Book), nameof(Book.CollectedAt))]
    public void 日時は保存の直前にUTCへそろえる(Type entity, string propertyName)
    {
        using var db = Context();
        var converter = db.Model.FindEntityType(entity)!.FindProperty(propertyName)!.GetValueConverter();

        Assert.NotNull(converter);
        var jst = new DateTimeOffset(2026, 8, 10, 19, 0, 0, JapanTime.Offset);
        var stored = Assert.IsType<DateTimeOffset>(converter.ConvertToProvider(jst));

        Assert.Equal(TimeSpan.Zero, stored.Offset);
        // 時点は変えない(9 時間ずらすのではなく、同じ瞬間を UTC で表す)
        Assert.Equal(jst, stored);
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
