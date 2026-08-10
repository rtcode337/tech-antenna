using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存するシークレットストア。値は保護済みの文字列で受け取る。</summary>
public class EfSecretStore(
    IDbContextFactory<TechAntennaDbContext> contextFactory,
    TimeProvider timeProvider) : ISecretStore
{
    public async Task<IReadOnlyList<Secret>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Secrets.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task SetAsync(
        string name, string protectedValue, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.Secrets.FindAsync([name], cancellationToken);
        if (existing is null)
        {
            db.Secrets.Add(new Secret
            {
                Name = name,
                Value = protectedValue,
                UpdatedAt = timeProvider.GetUtcNow(),
            });
        }
        else
        {
            existing.Value = protectedValue;
            existing.UpdatedAt = timeProvider.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        await db.Secrets.Where(s => s.Name == name).ExecuteDeleteAsync(cancellationToken);
    }
}
