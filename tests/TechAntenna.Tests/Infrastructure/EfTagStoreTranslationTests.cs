using Microsoft.EntityFrameworkCore;
using TechAntenna.Infrastructure.Persistence;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// タグの仕分け対象を選ぶ問い合わせが SQL に翻訳できるかを、DB につながずに確かめる
/// (<c>ToQueryString</c> は接続を開かない。<see cref="EfEventStoreTranslationTests"/> と同じ流儀)。
///
/// 件数の条件は計算プロパティ(<c>Tag.TotalCount</c>)では書けない。書くと
/// 「The LINQ expression could not be translated」で仕分けのボタンが落ちる —— InMemory の
/// ストアでは通るので、実 DB につなぐまで気づけない型の失敗。ここで見張っておく。
/// </summary>
public class EfTagStoreTranslationTests
{
    static TechAntennaDbContext Context() =>
        new(new DbContextOptionsBuilder<TechAntennaDbContext>()
            // 接続はしないので、実在しないホストでよい
            .UseNpgsql("Host=localhost;Database=none")
            .Options);

    [Fact]
    public void 仕分け対象の問い合わせが翻訳できる()
    {
        using var db = Context();
        var now = new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

        // ストアが実際に使う問い合わせをそのまま掛ける(書き写すと、直したときにずれる)
        var sql = EfTagStore.PendingQuery(db, now).ToQueryString();

        // 件数の足し算が SQL 側に出ていること(＝紐づくデータの無い保留を DB で落とせている)
        Assert.Contains("\"ArticleCount\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"BookCount\"", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }
}
