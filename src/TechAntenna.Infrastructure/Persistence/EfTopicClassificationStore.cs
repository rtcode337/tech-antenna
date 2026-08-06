using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>LLM によるタグ分類の PostgreSQL 実装。</summary>
public class EfTopicClassificationStore(
    IDbContextFactory<TechAntennaDbContext> contextFactory) : ITopicClassificationStore
{
    public async Task<IReadOnlyList<TopicClassification>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.TopicClassifications.ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        IReadOnlyList<TopicClassification> classifications,
        CancellationToken cancellationToken = default)
    {
        if (classifications.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var tags = classifications.Select(c => c.Tag).ToList();
        var existing = await db.TopicClassifications
            .Where(c => tags.Contains(c.Tag))
            .ToDictionaryAsync(c => c.Tag, cancellationToken);

        foreach (var classification in classifications)
        {
            if (existing.TryGetValue(classification.Tag, out var stored))
            {
                stored.Kind = classification.Kind;
                stored.TargetKey = classification.TargetKey;
                stored.Display = classification.Display;
                stored.ParentKey = classification.ParentKey;
                stored.ClassifiedAt = classification.ClassifiedAt;
            }
            else
            {
                db.TopicClassifications.Add(classification);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
