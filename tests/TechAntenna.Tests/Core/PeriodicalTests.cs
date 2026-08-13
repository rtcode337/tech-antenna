using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class PeriodicalTests
{
    // 実際に一覧を占めていた形。号ごとに別の本として出てくるので数で押し切られる
    [Theory]
    [InlineData("週刊アスキー No.1500 2026年8月11日発行")]
    [InlineData("月刊 I/O 2026年 09 月号")]
    [InlineData("日経ソフトウエア 2026年9月号")]
    [InlineData("Software Design 2026年7月号")]
    [InlineData("日経Linux 2026年 05 月号")]
    [InlineData("生成AI完全ガイド (日経BPムック)")]
    [InlineData("Interface 別冊 AIエッジ")]
    [InlineData("トランジスタ技術 増刊")]
    [InlineData("WEB+DB PRESS 総集編")]
    public void 雑誌やムックと判定する(string title) => Assert.True(Periodical.IsLikely(title));

    [Theory]
    [InlineData("リーダブルコード")]
    [InlineData("達人プログラマー 熟達に向けたあなたの旅")]
    [InlineData("ゼロから作るDeep Learning ❺")]
    [InlineData("増補改訂版 詳解 TCP/IP")] // 「増刊」ではない
    [InlineData("2026年版 応用情報技術者試験 対策")] // 年だけでは号数にしない
    [InlineData("大規模言語モデル入門")]
    public void 書籍は落とさない(string title) => Assert.False(Periodical.IsLikely(title));

    [Fact]
    public void タイトルが無ければ判定しない()
    {
        // 定番の収集は ISBN だけで本を組み立てる(タイトルは補完で入る)
        Assert.False(Periodical.IsLikely(""));
        Assert.False(Periodical.IsLikely((string?)null));
    }
}
