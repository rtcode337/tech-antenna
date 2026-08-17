using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>タグと仕分け状態の PostgreSQL 実装。</summary>
public class EfTagStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : ITagStore
{
    public async Task ObserveAsync(
        IReadOnlyList<TagObservation> observations,
        DateTimeOffset seenAt,
        bool resetMissing = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await db.Tags.ToDictionaryAsync(tag => tag.Key, cancellationToken);

        if (resetMissing)
        {
            var seen = observations.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var missing in stored.Values.Where(tag => !seen.Contains(tag.Key)))
            {
                InMemoryTagStore.Reset(missing);
            }
        }

        foreach (var observation in observations)
        {
            if (!stored.TryGetValue(observation.Key, out var tag))
            {
                tag = new Tag { Key = observation.Key, FirstSeenAt = seenAt };
                db.Tags.Add(tag);
            }

            InMemoryTagStore.Apply(tag, observation, seenAt);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DecideAsync(
        IReadOnlyList<TagDecision> decisions,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default)
    {
        if (decisions.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var keys = decisions.Select(decision => decision.Key).ToList();
        var stored = await db.Tags
            .Where(tag => keys.Contains(tag.Key))
            .ToDictionaryAsync(tag => tag.Key, cancellationToken);

        foreach (var decision in decisions)
        {
            if (stored.TryGetValue(decision.Key, out var tag))
            {
                InMemoryTagStore.Apply(tag, decision, decidedAt);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Tags.OrderBy(tag => tag.Key).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetPendingAsync(
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await PendingQuery(db, now).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 次に LLM へ聞くタグを目立つ順に引く問い合わせ。
    ///
    /// 未仕分け、または保留の期限切れ。**未仕分けは件数で足切りしない**
    /// (画面の「仕分けまち」がそのまま対象になるようにするため)。
    /// **保留は紐づくデータがある語だけ聞き直す** —— 判断できなかったうえに記事・
    /// イベント・書籍のどれにも付いていない語は、聞き直しても答えが変わる材料が無い。
    /// <c>Tag.TotalCount</c> は計算プロパティで翻訳できないので、列の足し算をそのまま書く。
    ///
    /// <b>問い合わせを切り出してあるのはテストのため。</b> InMemory のストアでは翻訳の失敗が
    /// 起きないので、<c>EfTagStoreTranslationTests</c> がここを <c>ToQueryString</c> に掛けて
    /// 見張っている(DB にはつながない)。
    /// </summary>
    public static IQueryable<Tag> PendingQuery(TechAntennaDbContext db, DateTimeOffset now) =>
        db.Tags
            .Where(tag => tag.Status == TagStatus.Pending
                || (tag.Status == TagStatus.Unresolved && tag.RetryAfter <= now
                    && tag.ArticleCount + tag.EventCount + tag.BookCount > 0))
            .OrderByDescending(tag =>
                tag.ArticleCount + tag.EventCount + tag.BookCount + tag.TrendScore)
            .ThenBy(tag => tag.Key);

    public async Task<int> RemoveAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return 0;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Tags.Where(tag => keys.Contains(tag.Key))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
