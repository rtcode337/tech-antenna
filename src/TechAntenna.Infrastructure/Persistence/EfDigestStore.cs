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

        return await db.Digests
            .Where(d => d.Scope == scope)
            .OrderByDescending(d => d.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
