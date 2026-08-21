using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// 興味トピックの書籍を1巡集める。選んだトピックごとに2つの経路を通す:
///
/// <list type="number">
/// <item>検索(<see cref="IBookCatalog"/>)—— トピックを検索語にして書誌を引く</item>
/// <item>引用(<see cref="IBookCitationSource"/>)—— そのトピックについて書かれた記事が
/// 本文で名指ししている本を拾う</item>
/// </list>
///
/// 同じジョブに入れてあるのは、<b>どちらも「選んだトピックを検索語にする」経路</b>だから
/// —— 起こし方も、選択が空なら1件も集まらないことも同じ。分けると押す場所が2つになる。
/// 一方で定番の推薦本(<see cref="ClassicsCollectionRunner"/>)は固定クエリで、
/// トピックの選択に依存しない別の軸なので、今までどおり別のジョブのまま。
///
/// 引用は<b>キーが要らない</b>ので、Google Books のキーを入れていない環境でも動く
/// (だから「検索が止まっている = 何も集まらない」ではない)。
/// </summary>
public class BookCollectionRunner(
    IEnumerable<IBookCatalog> catalogs,
    IEnumerable<IBookCitationSource> citationSources,
    IEnumerable<IBookEnricher> enrichers,
    SourceToggles toggles,
    IBookStore store,
    ITopicStore topicStore,
    TagObserver tagObserver,
    TopicCatalog topicCatalog,
    TimeProvider clock,
    IOptions<BooksOptions> options,
    ILogger<BookCollectionRunner> logger) : JobRunner
{
    readonly IBookCatalog? _catalog = catalogs.FirstOrDefault();
    readonly IReadOnlyList<IBookCitationSource> _citationSources = citationSources.ToList();

    public override string Name => "書籍の収集";

    public override bool IsConfigured => _catalog is not null || _citationSources.Count > 0;

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(cancellationToken), CollectionRunResult.Nothing, cancellationToken);

    async Task<CollectionRunResult> CollectAsync(CancellationToken cancellationToken)
    {
        // 止めていたら叩きに行かない(実行のたびに読むので再起動なしで効く)
        var searchCatalog = _catalog is not null && toggles.IsEnabled(SourceToggles.Book, _catalog.Name)
            ? _catalog
            : null;
        var citations = toggles.Enabled(_citationSources, SourceToggles.Citation, source => source.Name);
        if (searchCatalog is null && citations.Count == 0)
        {
            return CollectionRunResult.AllDisabled("書籍");
        }

        // 検索先へ同時アクセスしないよう、キーワードを1つずつ間隔を空けて処理する
        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenKeywordsSeconds);
        // 検索語。正式表記のほう(`生成ai` ではなく `生成AI`)
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
            if (searchCatalog is not null)
            {
                Progress = $"{searchCatalog.Name} で「{keyword}」の書籍を探しています…";
                var result = await SearchAsync(searchCatalog, keyword, cancellationToken);
                found += result.Fetched;
                added += result.Added;
                failed += result.FailedSources;
            }

            foreach (var source in citations)
            {
                Progress = $"{source.Name} で「{keyword}」の記事が挙げている本を探しています…";
                var result = await CollectCitationsAsync(source, keyword, cancellationToken);
                found += result.Fetched;
                added += result.Added;
                failed += result.FailedSources;
            }

            // 最後のキーワードの後は待たない(手動実行で無駄に待たせないため)。
            // 引用の経路は HttpClient の層で間隔を守るので、ここで待つのは検索のぶん
            if (i < keywords.Count - 1 && delay > TimeSpan.Zero && searchCatalog is not null)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        // 見つけたタグをタグの一覧へ反映する(状態は触らない)。
        // 推薦本(定番)の収集はここには無い —— 第三の軸なので ClassicsCollectionRunner が持つ
        await tagObserver.ObserveAsync(cancellationToken: cancellationToken);

        return new CollectionRunResult(found, added, failed);
    }

    /// <summary>キーワードで書誌を検索して保存する。</summary>
    async Task<CollectionRunResult> SearchAsync(
        IBookCatalog catalog, string keyword, CancellationToken cancellationToken)
    {
        try
        {
            var books = await catalog.SearchAsync(keyword, cancellationToken);
            books = ExcludePeriodicals(books, keyword);
            books = await EnrichAsync(books, cancellationToken);
            books = ApplyReviewFloor(books);

            var newlyAdded = await store.AddRangeAsync(books, cancellationToken);
            logger.LogInformation(
                "{Catalog} 「{Keyword}」: {Found} 件見つかり、うち {Added} 件を新規追加",
                catalog.Name, keyword, books.Count, newlyAdded);

            return new CollectionRunResult(books.Count, newlyAdded, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 1キーワードの失敗で巡回全体を止めない
            logger.LogError(ex, "「{Keyword}」の書籍収集に失敗", keyword);

            return new CollectionRunResult(0, 0, 1);
        }
    }

    /// <summary>
    /// そのトピックの記事が本文で名指ししている本を拾って保存する。
    ///
    /// 拾えるのは ISBN と出典記事だけなので、書誌は後段の補完(openBD・楽天)に任せる ——
    /// 定番の推薦本と同じ作り。タイトルが埋まらなかった本は保存しない(空行が並ぶだけになる)。
    /// タグは<b>検索語にしたトピック</b>で、検索で見つけた本と同じ規則
    /// —— これが無いと、その本は興味トピックの一覧のどのグループにも出てこない。
    /// </summary>
    async Task<CollectionRunResult> CollectCitationsAsync(
        IBookCitationSource source, string keyword, CancellationToken cancellationToken)
    {
        try
        {
            var citations = await source.FetchAsync(keyword, cancellationToken);
            if (citations.Count == 0)
            {
                return CollectionRunResult.Nothing;
            }

            var collectedAt = clock.GetUtcNow();
            // 保存済みの書影は引き継ぐ(毎回 Google Books へ引きに行かないため)
            var knownCovers = await KnownCovers.LoadAsync(store, cancellationToken);
            var books = citations
                .Select(citation => new Book
                {
                    // タイトルは openBD が埋める(この時点では ISBN しか分かっていない)
                    Title = "",
                    Isbn13 = citation.Isbn13,
                    CoverUrl = knownCovers.GetValueOrDefault(citation.Isbn13),
                    SourceName = source.Name,
                    CollectedAt = collectedAt,
                    CitedBy = citation.Articles,
                    Tags = topicCatalog.Normalize([keyword]),
                    RawTags = [keyword],
                })
                .ToList();

            // 雑誌・ムックは入れない。タイトルは補完で入るので、判定は補完の後
            var enriched = (await EnrichAsync(books, cancellationToken))
                .Where(book => book.Title.Length > 0 && !Periodical.IsLikely(book))
                .ToList();

            var newlyAdded = await store.AddRangeAsync(enriched, cancellationToken);
            logger.LogInformation(
                "{Source} 「{Keyword}」: 記事が挙げていた {Found} 冊、うち {Added} 冊を新規追加",
                source.Name, keyword, enriched.Count, newlyAdded);

            return new CollectionRunResult(enriched.Count, newlyAdded, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 1つの収集元・1キーワードの失敗で巡回全体を止めない
            logger.LogError(ex, "{Source} 「{Keyword}」の引用の収集に失敗", source.Name, keyword);

            return new CollectionRunResult(0, 0, 1);
        }
    }

    /// <summary>
    /// 雑誌・ムック・増刊を落とす(<see cref="Periodical"/>)。補完より前に落とす ——
    /// 落とすと決めた本に openBD や楽天へ問い合わせても、外部を余計に叩くだけ。
    ///
    /// 集めたいのは「その分野で読んでおくべき本」なのに、号を重ねるぶん数の多い雑誌が
    /// 検索結果を占めていた(実際に週刊アスキーの号が並んだ)。
    /// 落とした数はログに出す —— 黙って減らすと「検索したのに増えない」になる。
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
    /// レビューが取れた本だけが対象 —— 取れていない本(null)まで落とすと、
    /// 楽天のアプリ ID を設定していない環境で 1 冊も保存されなくなる。
    ///
    /// 掛けるのは検索で見つけた本だけ。引用は「記事が名指しした」ことが根拠なので、
    /// レビューの少なさで落とすと経路そのものが無意味になる。
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
                // 補完に失敗しても、拾えた書誌情報だけで保存を続ける
                logger.LogWarning(ex, "{Enricher} による書誌情報の補完に失敗", enricher.Name);
            }
        }

        return books;
    }
}
