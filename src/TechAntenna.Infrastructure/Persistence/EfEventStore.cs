using Microsoft.EntityFrameworkCore;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存するイベントストア。</summary>
public class EfEventStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : IEventStore
{
    public async Task<int> AddRangeAsync(IEnumerable<TechEvent> events, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var incoming = events.DistinctBy(e => e.Url).ToList();
        var urls = incoming.Select(e => e.Url).ToList();
        var existingUrls = await db.Events
            .Where(e => urls.Contains(e.Url))
            .Select(e => e.Url)
            .ToListAsync(cancellationToken);

        var newEvents = incoming.Where(e => !existingUrls.Contains(e.Url)).ToList();
        db.Events.AddRange(newEvents);

        // **既存のイベントは主催者と参加者数だけ取り込む。** 参加者数は開催が近づくほど
        // 増えるので、初回に集めた数のまま置くと注目度が古いままになる。書誌にあたる情報
        // (タイトル・日時・会場)を上書きしないのは書籍の BookMerge と同じ方針で、
        // **取れなかった回に null で上書きもしない**(TECH PLAY 経由で同じ URL を
        // 見かけたときに、connpass から取れていた数を消さないため)
        if (existingUrls.Count > 0)
        {
            var byUrl = incoming.ToDictionary(e => e.Url);
            foreach (var existing in await db.Events
                .Where(e => existingUrls.Contains(e.Url))
                .ToListAsync(cancellationToken))
            {
                var fresh = byUrl[existing.Url];
                existing.Organizer = fresh.Organizer ?? existing.Organizer;
                existing.ParticipantCount = fresh.ParticipantCount ?? existing.ParticipantCount;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return newEvents.Count;
    }

    public async Task<IReadOnlyList<TechEvent>> GetUpcomingAsync(DateTimeOffset from, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Events
            .Where(e => e.StartsAt >= from)
            .OrderBy(e => e.StartsAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TechEvent>> GetInRangeAsync(
        DateTimeOffset from, DateTimeOffset to, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Events
            .Where(e => e.StartsAt >= from && e.StartsAt < to)
            .OrderBy(e => e.StartsAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizerCount>> GetOrganizerCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await OrganizerCountQuery(db).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 主催者ごとの件数。Organizer は普通の text 列(値変換の掛かった <c>Tags</c> と違う)なので
    /// 集計も LINQ で書けるが、**並べ替えるまでは匿名型で持つこと** —— record のコンストラクタで
    /// 射影してから <c>OrderByDescending(o =&gt; o.Count)</c> と書くと、EF は <c>Count</c> を集計に
    /// 対応づけられず「The LINQ expression could not be translated」で落ちる(実際に画面が
    /// エラーになった)。**匿名型ならメンバーを辿れる**ので、record にするのは並べ替えの後。
    ///
    /// <b>問い合わせを切り出してあるのはテストのため。</b> InMemory のストアでは翻訳の失敗が
    /// 起きないので、<c>EfEventStoreTranslationTests</c> がここを <c>ToQueryString</c> に掛けて
    /// 見張っている(DB にはつながない)。
    /// </summary>
    public static IQueryable<OrganizerCount> OrganizerCountQuery(TechAntennaDbContext db) =>
        db.Events
            .Where(e => e.Organizer != null && e.Organizer != "")
            .GroupBy(e => e.Organizer!)
            .Select(g => new { Organizer = g.Key, Count = g.Count() })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Organizer)
            .Select(row => new OrganizerCount(row.Organizer, row.Count));

    // タグ関連が生 SQL である理由は EfArticleStore を参照
    public async Task<IReadOnlyList<TechEvent>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Events
            .FromSql($"""SELECT * FROM "Events" WHERE "Tags" @> ARRAY[{tag}]::text[]""")
            .OrderBy(e => e.StartsAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Database
            .SqlQuery<TagCount>(
                $"""SELECT unnest("Tags") AS "Tag", COUNT(*)::int AS "Count" FROM "Events" GROUP BY 1""")
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // 全件を読み直す。個人運用の規模(数千件)を前提にページングはしていない
        var updated = 0;
        foreach (var techEvent in await db.Events.ToListAsync(cancellationToken))
        {
            var tags = catalog.Normalize(techEvent.RawTags);
            if (techEvent.Tags.SequenceEqual(tags, StringComparer.Ordinal))
            {
                continue;
            }

            techEvent.Tags = tags;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }
}
