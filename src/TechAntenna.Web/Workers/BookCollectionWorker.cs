using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Web.Workers;

/// <summary>設定されたキーワードで書籍を定期的に検索し、書誌情報を補ってストアへ保存する。</summary>
public class BookCollectionWorker(
    IBookCatalog catalog,
    IEnumerable<IBookEnricher> enrichers,
    IBookStore store,
    IOptions<BooksOptions> options,
    ILogger<BookCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(options.Value.IntervalHours);
        using var timer = new PeriodicTimer(interval);

        do
        {
            await CollectOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    async Task CollectOnceAsync(CancellationToken cancellationToken)
    {
        // 検索先へ同時アクセスしないよう、キーワードを1つずつ間隔を空けて処理する
        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenKeywordsSeconds);

        foreach (var keyword in options.Value.Keywords)
        {
            try
            {
                var books = await catalog.SearchAsync(keyword, cancellationToken);
                books = await EnrichAsync(books, cancellationToken);

                var added = await store.AddRangeAsync(books, cancellationToken);
                logger.LogInformation(
                    "{Catalog} 「{Keyword}」: {Found} 件見つかり、うち {Added} 件を新規追加",
                    catalog.Name, keyword, books.Count, added);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1キーワードの失敗で巡回全体を止めない
                logger.LogError(ex, "「{Keyword}」の書籍収集に失敗", keyword);
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    async Task<IReadOnlyList<Book>> EnrichAsync(
        IReadOnlyList<Book> books,
        CancellationToken cancellationToken)
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
