using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Web.Services;

/// <summary>設定されたキーワードで書籍を1巡検索し、書誌情報を補ってストアへ保存する。</summary>
public class BookCollectionRunner(
    IEnumerable<IBookCatalog> catalogs,
    IEnumerable<IBookEnricher> enrichers,
    SourceToggles toggles,
    IBookStore store,
    ITopicStore topicStore,
    TagObserver tagObserver,
    IOptions<BooksOptions> options,
    ILogger<BookCollectionRunner> logger) : JobRunner
{
    readonly IBookCatalog? _catalog = catalogs.FirstOrDefault();

    public override string Name => "書籍の収集";

    public override bool IsConfigured => _catalog is not null;

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(_catalog!, cancellationToken),
            CollectionRunResult.Nothing, cancellationToken);

    async Task<CollectionRunResult> CollectAsync(
        IBookCatalog catalog, CancellationToken cancellationToken)
    {
        // **止めていたら叩きに行かない**(実行のたびに読むので再起動なしで効く)
        if (!toggles.IsEnabled(SourceToggles.Book, catalog.Name))
        {
            return CollectionRunResult.AllDisabled("書籍");
        }

        // 検索先へ同時アクセスしないよう、キーワードを1つずつ間隔を空けて処理する
        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenKeywordsSeconds);
        // Google Books へ投げる検索語。**正式表記のほう**(`生成ai` ではなく `生成AI`)
        var keywords = (await topicStore.GetSelectedAsync(cancellationToken))
            .Select(topic => topic.Display).ToList();
        if (keywords.Count == 0)
        {
            // 何も集まらない理由を文言にする(論文・イベントと同じ扱い)
            return CollectionRunResult.NoTopics("書籍");
        }
        int found = 0, added = 0, failed = 0;

        for (var i = 0; i < keywords.Count; i++)
        {
            var keyword = keywords[i];
            try
            {
                var books = await catalog.SearchAsync(keyword, cancellationToken);
                books = ExcludePeriodicals(books, keyword);
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

        // 見つけたタグをタグの一覧へ反映する(状態は触らない)。
        // 推薦本(定番)の収集はここには無い —— 第三の軸なので ClassicsCollectionRunner が持つ
        await tagObserver.ObserveAsync(cancellationToken: cancellationToken);

        return new CollectionRunResult(found, added, failed);
    }

    /// <summary>
    /// 雑誌・ムック・増刊を落とす(<see cref="Periodical"/>)。**補完より前に落とす** ——
    /// 落とすと決めた本に openBD や楽天へ問い合わせても、外部を余計に叩くだけ。
    ///
    /// 集めたいのは「その分野で読んでおくべき本」なのに、号を重ねるぶん数の多い雑誌が
    /// 検索結果を占めていた(実際に週刊アスキーの号が並んだ)。
    /// **落とした数はログに出す** —— 黙って減らすと「検索したのに増えない」になる。
    /// </summary>
    IReadOnlyList<Book> ExcludePeriodicals(IReadOnlyList<Book> books, string keyword)
    {
        var kept = books.Where(book => !Periodical.IsLikely(book)).ToList();
        if (kept.Count < books.Count)
        {
            logger.LogInformation(
                "「{Keyword}」: 雑誌・ムックらしい {Excluded} 件を除いた(残り {Kept} 件)",
                keyword, books.Count - kept.Count, kept.Count);
        }

        return kept;
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
        // 補完も1つずつ止められる(止めたものは叩きに行かない)
        foreach (var enricher in toggles.Enabled(enrichers, SourceToggles.Enricher, e => e.Name))
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
