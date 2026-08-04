using Microsoft.EntityFrameworkCore;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存するトピックストア。</summary>
public class EfTopicStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : ITopicStore
{
    public async Task UpsertAsync(
        IReadOnlyList<TopicUpdate> topics,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await db.Topics.ToDictionaryAsync(topic => topic.Tag, cancellationToken);

        // 今回現れなかったトピックは話題度だけ 0 にする。**行は消さない** ——
        // 消すと選択(IsSelected)ごと失われ、収集キーワードが空になって収集が止まる
        var seen = topics.Select(topic => topic.Tag).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in stored.Values.Where(topic => !seen.Contains(topic.Tag)))
        {
            missing.TrendScore = 0;
            missing.SourceCount = 0;
            // 在庫の件数も落とす —— 別名がまとまってタグが消えたときに古い件数が残るため
            missing.ArticleCount = 0;
            missing.EventCount = 0;
            missing.BookCount = 0;
        }

        foreach (var topic in topics)
        {
            if (!stored.TryGetValue(topic.Tag, out var row))
            {
                row = new StoredTopic { Tag = topic.Tag };
                db.Topics.Add(row);
            }

            InMemoryTopicStore.Apply(row, topic, collectedAt);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredTopic>> GetTopicsAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Topics
            // 選択済みは話題度が 0 でも押し出されないよう先頭に固定する
            .OrderByDescending(topic => topic.IsSelected)
            .ThenByDescending(topic => topic.TrendScore)
            .ThenBy(topic => topic.Tag)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateSelectionAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        var selected = TagNormalizer.Normalize(tags);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        await db.Topics.ExecuteUpdateAsync(
            set => set.SetProperty(topic => topic.IsSelected, false), cancellationToken);

        if (selected.Count > 0)
        {
            await db.Topics.Where(topic => selected.Contains(topic.Tag))
                .ExecuteUpdateAsync(set => set.SetProperty(topic => topic.IsSelected, true), cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SelectedTopic>> GetSelectedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Topics
            .Where(topic => topic.IsSelected)
            .OrderBy(topic => topic.Tag)
            .Select(topic => new SelectedTopic(topic.Tag, topic.Display == "" ? topic.Tag : topic.Display))
            .ToListAsync(cancellationToken);
    }
}
