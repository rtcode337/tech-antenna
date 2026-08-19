using System.Text.RegularExpressions;

namespace TechAntenna.Core.Models;

/// <summary>
/// その本が<b>定期刊行物(雑誌・ムック・増刊)らしいか</b>の判定。
///
/// 集めたいのは「その分野で読んでおくべき本」なのに、キーワード検索の結果は
/// 号を重ねるぶんだけ数が多い雑誌に埋まる(実際に週刊アスキーの号が並んだ)。
/// 雑誌は号ごとに別の本として出てくるうえ、レビューも推薦も付きにくいので、
/// 並べ替えでも沈まずに一覧を占める。
///
/// Google Books の <c>printType=books</c> だけでは足りない。あちらが弾くのは
/// Google が雑誌として登録しているものだけで、日本のムック・別冊・増刊は
/// ISBN の付いた書籍として返ってくる。そこでタイトルの型で補う。
///
/// 判定は「らしさ」なので、機械的に確実なものだけを見る。迷う語(「Vol.」など。
/// 巻数を持つ書籍もある)は入れない —— 読むべき本を取りこぼすほうが害が大きい。
/// </summary>
public static class Periodical
{
    /// <summary>
    /// 号数の型。「2026年9月号」「9月号」「2026年 09 月号」に当たる。
    /// これが一番効く —— 定期刊行誌のタイトルはほぼこの形で終わる。
    /// </summary>
    static readonly Regex IssueNumber = new(
        @"(\d{1,2}\s*月号)|(\d{4}\s*年\s*\d{1,2}\s*月)", RegexOptions.Compiled);

    /// <summary>
    /// 刊行の頻度・形態を表す語。タイトルのどこにあっても雑誌扱いにする
    /// (「日経ソフトウエア別冊」のように後ろに付くこともある)。
    /// </summary>
    static readonly string[] Markers =
    [
        "週刊", "月刊", "隔月刊", "季刊", "旬刊", "日刊",
        "増刊", "別冊", "ムック", "MOOK", "総集編", "バックナンバー",
    ];

    /// <summary>その本が定期刊行物らしいか。タイトルだけで判定する。</summary>
    public static bool IsLikely(Book book) => IsLikely(book.Title);

    /// <summary>タイトルが定期刊行物らしいか。</summary>
    public static bool IsLikely(string? title)
    {
        if (title is not { Length: > 0 })
        {
            return false;
        }

        return Markers.Any(marker => title.Contains(marker, StringComparison.OrdinalIgnoreCase))
            || IssueNumber.IsMatch(title);
    }
}
