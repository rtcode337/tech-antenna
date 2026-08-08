using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Web.Services;

/// <summary>設定されたキーワードで書籍を1巡検索し、書誌情報を補ってストアへ保存する。</summary>
public class BookCollectionRunner(
    IEnumerable<IBookCatalog> catalogs,
    IEnumerable<IBookEnricher> enrichers,
    IEnumerable<IBookRecommendationSource> recommendationSources,
    IBookStore store,
    ITopicStore topicStore,
    TagObserver tagObserver,
    IOptions<BooksOptions> options,
    TimeProvider clock,
    ILogger<BookCollectionRunner> logger) : JobRunner
{
    readonly IBookCatalog? _catalog = catalogs.FirstOrDefault();
    readonly IReadOnlyList<IBookRecommendationSource> _recommendationSources = recommendationSources.ToList();

    public override string Name => "書籍の収集";

    public override bool IsConfigured => _catalog is not null;

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(_catalog!, cancellationToken),
            CollectionRunResult.Nothing, cancellationToken);

    async Task<CollectionRunResult> CollectAsync(
        IBookCatalog catalog, CancellationToken cancellationToken)
    {
        // 検索先へ同時アクセスしないよう、キーワードを1つずつ間隔を空けて処理する
        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenKeywordsSeconds);
        // Google Books へ投げる検索語。**正式表記のほう**(`生成ai` ではなく `生成AI`)
        var keywords = (await topicStore.GetSelectedAsync(cancellationToken))
            .Select(topic => topic.Display).ToList();
        if (keywords.Count == 0)
        {
            return CollectionRunResult.Nothing;
        }
        int found = 0, added = 0, failed = 0;

        for (var i = 0; i < keywords.Count; i++)
        {
            var keyword = keywords[i];
            try
            {
                var books = await catalog.SearchAsync(keyword, cancellationToken);
                books = await EnrichAsync(books, cancellationToken);
                books = ApplyReviewFloor(books);

                var newlyAdded = await store.AddRangeAsync(books, cancellationToken);
                found += books.Count;
                added += newlyAdded;
                logger.LogInformation(
                    "{Catalog} 「{Keyword}」: {Found} 件見つかり、うち {Added} 件を新規追加",
                    catalog.Name, keyword, books.Count, newlyAdded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1キーワードの失敗で巡回全体を止めない
                failed++;
                logger.LogError(ex, "「{Keyword}」の書籍収集に失敗", keyword);
            }

            // 最後のキーワードの後は待たない(手動実行で無駄に待たせないため)
            if (i < keywords.Count - 1 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        var recommended = await CollectRecommendationsAsync(cancellationToken);

        // 見つけたタグをタグの一覧へ反映する(状態は触らない)

        await tagObserver.ObserveAsync(cancellationToken: cancellationToken);


        return new CollectionRunResult(found + recommended.Found, added + recommended.Added, failed + recommended.Failed);
    }

    /// <summary>
    /// 「読むべき本」を挙げた記事から薦められている本を拾う。**検索とは独立した経路**で、
    /// トピックの選択とも関係しない —— 定番書はトピックの流行り廃りとは別に薦められ続けるため。
    ///
    /// 拾えるのは ISBN だけなので、書誌情報は後段の補完(openBD)に任せる。
    /// **補完できずタイトルが空のままの本は保存しない**(画面に空行が並ぶだけになる)。
    /// </summary>
    async Task<(int Found, int Added, int Failed)> CollectRecommendationsAsync(
        CancellationToken cancellationToken)
    {
        int found = 0, added = 0, failed = 0;

        foreach (var source in _recommendationSources)
        {
            try
            {
                var recommendations = await source.FetchAsync(cancellationToken);
                if (recommendations.Count == 0)
                {
                    continue;
                }

                var collectedAt = clock.GetUtcNow();
                var books = recommendations
                    .Select(recommendation => new Book
                    {
                        // タイトルは openBD が埋める(この時点では ISBN しか分かっていない)
                        Title = "",
                        Isbn13 = recommendation.Isbn13,
                        SourceName = source.Name,
                        CollectedAt = collectedAt,
                        RecommendedBy = recommendation.ArticleUrls,
                    })
                    .ToList();

                var enriched = (await EnrichAsync(books, cancellationToken))
                    .Where(book => book.Title.Length > 0)
                    .ToList();

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

        return (found, added, failed);
    }

    /// <summary>
    /// レビューが少なすぎる本を落とす(`Books:MinReviewCount`、既定 0 = 落とさない)。
    /// **レビューが取れた本だけが対象** —— 取れていない本(null)まで落とすと、
    /// 楽天のアプリ ID を設定していない環境で 1 冊も保存されなくなる。
    /// </summary>
    IReadOnlyList<Book> ApplyReviewFloor(IReadOnlyList<Book> books)
    {
        var floor = options.Value.MinReviewCount;

        return floor <= 0
            ? books
            : books.Where(book => book.ReviewCount is not { } count || count >= floor).ToList();
    }

    async Task<IReadOnlyList<Book>> EnrichAsync(
        IReadOnlyList<Book> books, CancellationToken cancellationToken)
    {
        foreach (var enricher in enrichers)
        {
            try
            {
                books = await enricher.EnrichAsync(books, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 補完に失敗しても、検索で得た書誌情報だけで保存を続ける
                logger.LogWarning(ex, "{Enricher} による書誌情報の補完に失敗", enricher.Name);
            }
        }

        return books;
    }
}
