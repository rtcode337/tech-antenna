using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存するダイジェストストア。</summary>
public class EfDigestStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : IDigestStore
{
    public async Task SaveAsync(Digest digest, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        db.Digests.Add(digest);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Digest?> GetLatestAsync(
        DigestScope scope, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // メインを優先して返す(同じ回にサブの分も入っているため)
        return await db.Digests
            .Where(d => d.Scope == scope)
            .OrderByDescending(d => d.GeneratedAt)
            .ThenByDescending(d => d.IsPrimary)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Digest>> GetLatestRunAsync(
        DigestScope scope, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // **最新の1件が属する回を丸ごと読む。** 時刻で寄せると、今日失敗した AI の
        // 前日ぶんが今日のものと並ぶ
        var latest = await db.Digests
            .Where(d => d.Scope == scope)
            .OrderByDescending(d => d.GeneratedAt)
            .Select(d => new { d.RunId })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return [];
        }

        return await db.Digests
            .Where(d => d.RunId == latest.RunId)
            .OrderByDescending(d => d.IsPrimary)
            .ThenBy(d => d.GeneratedAt)
            .ToListAsync(cancellationToken);
    }
}
