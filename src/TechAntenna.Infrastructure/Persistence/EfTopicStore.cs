using Microsoft.EntityFrameworkCore;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>語彙(トピック)の PostgreSQL 実装。</summary>
public class EfTopicStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : ITopicStore
{
    public async Task UpsertAsync(
        IReadOnlyList<Topic> topics,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await db.Topics.ToDictionaryAsync(topic => topic.Key, cancellationToken);

        // 今回現れなかったトピックは件数と話題度だけ 0 にする。行は消さない ——
        // 消すと選択(IsSelected)ごと失われ、収集キーワードが空になって収集が止まる
        var seen = topics.Select(topic => topic.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in stored.Values.Where(topic => !seen.Contains(topic.Key)))
        {
            InMemoryTopicStore.Reset(missing);
        }

        foreach (var topic in topics)
        {
            if (!stored.TryGetValue(topic.Key, out var row))
            {
                row = new Topic { Key = topic.Key };
                db.Topics.Add(row);
            }

            InMemoryTopicStore.Apply(row, topic, updatedAt);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(
        Topic topic, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await db.Topics.FirstOrDefaultAsync(t => t.Key == topic.Key, cancellationToken);
        if (stored is null)
        {
            stored = new Topic { Key = topic.Key };
            db.Topics.Add(stored);
        }

        InMemoryTopicStore.Apply(stored, topic, updatedAt);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Topic>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Topics
            // 選択済みは話題度が 0 でも先頭に固定する。並びは配下込みの話題度 ——
            // 単体だと構造の語(親)が沈み、ツリーが読みにくい
            .OrderByDescending(topic => topic.IsSelected)
            .ThenByDescending(topic => topic.SubtreeTrendScore)
            .ThenBy(topic => topic.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<Topic?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Topics.FirstOrDefaultAsync(topic => topic.Key == key, cancellationToken);
    }

    public async Task<int> RemoveAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return 0;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // 選択済みは消さない(収集キーワードごと失われるため)
        return await db.Topics
            .Where(topic => keys.Contains(topic.Key) && !topic.IsSelected)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task UpdateSelectionAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        var selected = TagNormalizer.Normalize(keys);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        await db.Topics.ExecuteUpdateAsync(
            set => set.SetProperty(topic => topic.IsSelected, false), cancellationToken);

        if (selected.Count > 0)
        {
            await db.Topics.Where(topic => selected.Contains(topic.Key))
                .ExecuteUpdateAsync(
                    set => set.SetProperty(topic => topic.IsSelected, true), cancellationToken);
        }
    }

    public async Task<bool> SetSelectedAsync(
        string key, bool selected, CancellationToken cancellationToken = default)
    {
        // 画面から来るのは正規化済みのキーだが、念のため同じ規則を通してから当てる
        var normalized = TagNormalizer.Normalize([key]);
        if (normalized.Count != 1)
        {
            return false;
        }

        // 添字のままクエリに書かず、いったん変数へ出す(EF がパラメータとして扱えるように)
        var target = normalized[0];
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // その 1 行だけを更新する。一覧を丸ごと置き換える UpdateSelectionAsync と違い、
        // 画面に出ていない行の選択には触らない
        var updated = await db.Topics
            .Where(topic => topic.Key == target)
            .ExecuteUpdateAsync(
                set => set.SetProperty(topic => topic.IsSelected, selected), cancellationToken);

        return updated > 0;
    }

    public async Task<IReadOnlyList<SelectedTopic>> GetSelectedAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Topics
            .Where(topic => topic.IsSelected)
            .OrderBy(topic => topic.Key)
            .Select(topic => new SelectedTopic(
                topic.Key, topic.Display == "" ? topic.Key : topic.Display, topic.English))
            .ToListAsync(cancellationToken);
    }
}
