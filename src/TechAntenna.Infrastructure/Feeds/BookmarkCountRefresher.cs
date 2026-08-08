using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>
/// 直近の記事・ニュースのはてなブックマーク数を、件数 API でまとめて引き直してストアへ保存する。
///
/// **トレンドの収集と、トピック収集の話題度の集計の両方から使う。** トピック収集側でも
/// 引き直すのは、話題度の材料が「最後に記事を収集した時点の値」に依存しないようにするため
/// (Qiita のいいねはその場で取るのに、はてブだけ古いままだと材料の鮮度が揃わない)。
/// 件数は 50 URL ずつの一括なので、1回の引き直しは数リクエストで済む。
///
/// **論文は対象外** —— はてブにほぼ載らず、リクエストを増やすだけになる。
/// </summary>
public class BookmarkCountRefresher(
    IArticleStore articleStore,
    HatenaBookmarkCounts bookmarkCounts,
    TimeProvider timeProvider)
{
    /// <summary>種別ごとに何件まで引き直すか(50 件 = 1 リクエスト)。</summary>
    const int Limit = 200;

    /// <summary>引き直す公開からの日数。古い記事は数字がほぼ動かない。</summary>
    const int WindowDays = 7;

    /// <summary>直近の記事・ニュースの件数を引き直し、値が変わった件数を返す。</summary>
    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var threshold = timeProvider.GetUtcNow().AddDays(-WindowDays);
        var targets = new List<Article>();
        foreach (var kind in (ArticleKind[])[ArticleKind.Article, ArticleKind.News])
        {
            targets.AddRange((await articleStore.GetRecentAsync(Limit, kind, cancellationToken))
                .Where(a => (a.PublishedAt ?? a.CollectedAt) >= threshold));
        }

        if (targets.Count == 0)
        {
            return 0;
        }

        var counts = await bookmarkCounts.FetchAsync(
            targets.Select(a => a.Url).ToList(), cancellationToken);
        var updates = targets
            .Where(a => counts.ContainsKey(a.Url.ToString()))
            .Select(a => (a.Id, counts[a.Url.ToString()]))
            .ToList();

        return await articleStore.UpdateBookmarkCountsAsync(updates, cancellationToken);
    }
}
