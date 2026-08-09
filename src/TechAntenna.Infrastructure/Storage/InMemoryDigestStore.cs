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

    public Task<Digest?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _digests.OrderByDescending(d => d.GeneratedAt).FirstOrDefault());
        }
    }
}
