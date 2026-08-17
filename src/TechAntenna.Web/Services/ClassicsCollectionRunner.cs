using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// 定番(「読むべき本」と名指しされ続けるもの)の収集。
/// 「読むべき技術書」を挙げた記事から、薦められている本を拾う(<see cref="IBookRecommendationSource"/>)。
///
/// **書籍の収集(<see cref="BookCollectionRunner"/>)から独立させてある。** あちらは
/// 選んだトピックを検索語にする「興味トピック」の軸で、こちらは<b>第三の軸(定番)</b> ——
/// 新着(トレンド)でも興味の検索でもなく、固定クエリで定評を掘る。
/// 同居していた頃は、トピック選択が空だと検索の手前で return して、
/// **選択と関係しないはずの推薦本まで 1 冊も集まらなかった**。
/// </summary>
public class ClassicsCollectionRunner(
    IEnumerable<IBookRecommendationSource> sources,
    IEnumerable<IBookEnricher> enrichers,
    IBookStore store,
    TagObserver tagObserver,
    TopicCatalog catalog,
    TimeProvider clock,
    ILogger<ClassicsCollectionRunner> logger) : JobRunner
{
    readonly IReadOnlyList<IBookRecommendationSource> _sources = sources.ToList();

    /// <summary>書影の引き継ぎのために読み込む保存済みの冊数。推薦本は多くても数百冊。</summary>
    const int StoredBookLimit = 2000;

    public override string Name => "定番の収集";

    public override bool IsConfigured => _sources.Count > 0;

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(cancellationToken), CollectionRunResult.Nothing, cancellationToken);

    /// <summary>
    /// 薦められている本を拾って保存する。拾えるのは ISBN だけなので、書誌情報は
    /// 後段の補完(openBD・楽天)に任せる。**補完できずタイトルが空のままの本は保存しない**
    /// (画面に空行が並ぶだけになる)。
    /// </summary>
    async Task<CollectionRunResult> CollectAsync(CancellationToken cancellationToken)
    {
        int found = 0, added = 0, failed = 0;

        foreach (var source in _sources)
        {
            try
            {
                Progress = $"{source.Name} から推薦記事を読んでいます…";
                var recommendations = await source.FetchAsync(cancellationToken);
                if (recommendations.Count == 0)
                {
                    continue;
                }

                var collectedAt = clock.GetUtcNow();
                // **保存済みの書影は引き継ぐ。** 拾えるのは ISBN だけなので毎回まっさらな本を
                // 組み立てることになるが、そのまま補完へ渡すと**同じ本の書影を毎回 Google Books へ
                // 問い合わせる**(1 冊 1 リクエスト・無料枠 1 日 1,000)。一度埋まった本は
                // 二度と引かないようにする
                var knownCovers = await KnownCoversAsync(cancellationToken);
                var books = recommendations
                    .Select(recommendation => new Book
                    {
                        // タイトルは openBD が埋める(この時点では ISBN しか分かっていない)
                        Title = "",
                        Isbn13 = recommendation.Isbn13,
                        CoverUrl = knownCovers.GetValueOrDefault(recommendation.Isbn13),
                        SourceName = source.Name,
                        CollectedAt = collectedAt,
                        RecommendedBy = recommendation.Articles,
                    })
                    .ToList();

                Progress = $"{books.Count} 冊の書誌情報を補完しています…";
                // 雑誌・ムックは定番にも入れない(記事が号を名指しすることがある)。
                // タイトルは補完で入るので、判定は補完の後
                var enriched = (await EnrichAsync(books, cancellationToken))
                    .Where(book => book.Title.Length > 0 && !Periodical.IsLikely(book))
                    .ToList();

                // タイトルが入ったのでトピックのタグを付ける(記事と同じ規則)。
                // 推薦本は検索キーワードを持たないので、これが唯一のタグの源 ——
                // 無いとトピックの詳細にも /books の分類にも乗らない
                foreach (var book in enriched)
                {
                    book.Tags = catalog.Normalize(catalog.FindIn(book.Title));
                }

                var newlyAdded = await store.AddRangeAsync(enriched, cancellationToken);
                found += enriched.Count;
                added += newlyAdded;
                logger.LogInformation(
                    "{Source}: {Found} 冊が薦められていて、うち {Added} 冊を新規追加",
                    source.Name, enriched.Count, newlyAdded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1つの収集元の失敗で全体を止めない
                failed++;
                logger.LogError(ex, "{Source} からの推薦本の収集に失敗", source.Name);
            }
        }

        // 見つけたタグをタグの一覧へ反映する(状態は触らない)
        await tagObserver.ObserveAsync(cancellationToken: cancellationToken);

        return new CollectionRunResult(found, added, failed);
    }

    /// <summary>
    /// 保存済みの本の ISBN → 書影 URL。**同じ本の書影を毎回外部へ問い合わせないため**に使う。
    /// 推薦本は多くても数百冊なので全件読んで構わない。
    /// </summary>
    async Task<IReadOnlyDictionary<string, Uri?>> KnownCoversAsync(CancellationToken cancellationToken)
    {
        var stored = await store.GetRecentAsync(StoredBookLimit, cancellationToken);

        return stored
            .Where(book => book.CoverUrl is not null && book.Isbn13 is { Length: > 0 })
            .GroupBy(book => book.Isbn13!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().CoverUrl, StringComparer.Ordinal);
    }

    async Task<IReadOnlyList<Book>> EnrichAsync(
        IReadOnlyList<Book> books, CancellationToken cancellationToken)
    {
        foreach (var enricher in enrichers)
        {
            try
            {
                // **どの補完で待っているのかを画面に出す。** 書影の補完は 1 冊 1 リクエスト・
                // 1 秒間隔なので数百冊なら数分かかる —— 名前が出ていないと止まって見える
                Progress = $"{enricher.Name} で {books.Count} 冊を補完しています…";
                books = await enricher.EnrichAsync(books, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 補完に失敗しても、拾えた書誌情報だけで保存を続ける
                logger.LogWarning(ex, "{Enricher} による書誌情報の補完に失敗", enricher.Name);
            }
        }

        return books;
    }
}
