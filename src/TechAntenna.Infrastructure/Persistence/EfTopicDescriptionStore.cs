using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>トピックの一言説明の PostgreSQL 実装。</summary>
public class EfTopicDescriptionStore(
    IDbContextFactory<TechAntennaDbContext> contextFactory) : ITopicDescriptionStore
{
    public async Task<IReadOnlyList<TopicDescription>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.TopicDescriptions.ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        IReadOnlyList<TopicDescription> descriptions,
        CancellationToken cancellationToken = default)
    {
        if (descriptions.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var keys = descriptions.Select(description => description.Key).ToList();
        var existing = await db.TopicDescriptions
            .Where(description => keys.Contains(description.Key))
            .ToDictionaryAsync(description => description.Key, cancellationToken);

        foreach (var description in descriptions)
        {
            if (existing.TryGetValue(description.Key, out var stored))
            {
                stored.Text = description.Text;
                stored.DescribedAt = description.DescribedAt;
            }
            else
            {
                db.TopicDescriptions.Add(description);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
