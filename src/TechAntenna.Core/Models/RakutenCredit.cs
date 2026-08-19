namespace TechAntenna.Core.Models;

/// <summary>
/// 楽天ウェブサービスのクレジット表記が要るかの判定。
///
/// 楽天から取ったデータを画面に出すときは表記が要る(利用規約 Article 13)。
/// かつては「レビューを出しているか」だけで判定していたが、書影も楽天由来のことがある
/// (openBD が技術書の書影をほとんど持たないので、楽天の応答から埋めている)ので両方を見る。
/// </summary>
public static class RakutenCredit
{
    /// <summary>楽天の画像 URL のホスト。増えたらここに足す —— 判定を外すと表記が消える。</summary>
    static readonly string[] ImageHosts = ["rakuten.co.jp", "rakuten.com"];

    /// <summary>その一覧に楽天由来のデータ(レビューか書影)が含まれているか。</summary>
    public static bool IsRequiredFor(IEnumerable<Book> books) => books.Any(IsRequiredFor);

    /// <summary>
    /// その本が楽天由来のデータを<b>実際に見せているか</b>。レビューは楽天からしか取っていないが、
    /// 0 件のときは画面に出ないので数に入れない(<c>BookItem</c> の出し分けと同じ条件)。
    /// 書影は URL のホストで見分ける —— どこから埋めたかを列に持っていないため。
    /// <see cref="Book.CoverUrl"/> は表示のたびに読む値なので、ホストで見れば画面に出るものと食い違わない。
    /// </summary>
    public static bool IsRequiredFor(Book book) =>
        book.ReviewCount is > 0 || IsRakutenImage(book.CoverUrl);

    static bool IsRakutenImage(Uri? cover) => cover is not null
        && ImageHosts.Any(host =>
            cover.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || cover.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
}
