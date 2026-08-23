using Microsoft.EntityFrameworkCore;
using TechAntenna.Infrastructure.Persistence;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// 興味トピックの並び(<c>SortOrder</c>)の問い合わせが SQL に翻訳できるかを、
/// DB につながずに確かめる(<c>ToQueryString</c> は接続を開かない。
/// <see cref="EfTagStoreTranslationTests"/> と同じ流儀)。
///
/// 「未指定(0)を後ろへ回す」を真偽値の並べ替え(<c>OrderBy(t =&gt; t.SortOrder == 0)</c>)で
/// 書いている。InMemory のストアは LINQ をそのまま実行するので翻訳の失敗が起きず、
/// 実 DB につないだときだけ画面が 500 になる型の失敗になる。
/// </summary>
public class EfTopicStoreTranslationTests
{
    static TechAntennaDbContext Context() =>
        new(new DbContextOptionsBuilder<TechAntennaDbContext>()
            // 接続はしないので、実在しないホストでよい
            .UseNpgsql("Host=localhost;Database=none")
            .Options);

    [Fact]
    public void 選択済みを並び順で引く問い合わせが翻訳できる()
    {
        using var db = Context();

        // ストアが実際に使う問い合わせをそのまま掛ける(書き写すと、直したときにずれる)
        var sql = EfTopicStore.Ordered(db).ToQueryString();

        Assert.Contains("\"SortOrder\"", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        // 未指定(0)を後ろへ回す判定が SQL 側に出ていること
        Assert.Contains("= 0", sql, StringComparison.Ordinal);
    }
}
