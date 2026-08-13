using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>
/// メモリ上のダイジェストストア。DB 接続なしで動かすときのつなぎで、
/// プロセスを再起動すると消える。
/// </summary>
public class InMemoryDigestStore : IDigestStore
{
    readonly object _gate = new();
    readonly List<Digest> _digests = [];

    public Task SaveAsync(Digest digest, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _digests.Add(digest);
            return Task.CompletedTask;
        }
    }

    public Task<Digest?> GetLatestAsync(
        DigestScope scope, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_digests
                .Where(d => d.Scope == scope)
                .OrderByDescending(d => d.GeneratedAt)
                .ThenByDescending(d => d.IsPrimary)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<Digest>> GetLatestRunAsync(
        DigestScope scope, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var latest = _digests
                .Where(d => d.Scope == scope)
                .OrderByDescending(d => d.GeneratedAt)
                .FirstOrDefault();

            IReadOnlyList<Digest> run = latest is null
                ? []
                : _digests
                    .Where(d => d.RunId == latest.RunId)
                    .OrderByDescending(d => d.IsPrimary)
                    .ThenBy(d => d.GeneratedAt)
                    .ToList();

            return Task.FromResult(run);
        }
    }
}
