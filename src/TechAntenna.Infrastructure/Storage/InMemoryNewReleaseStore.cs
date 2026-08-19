using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>
/// メモリ上の新刊ストア。DB 接続なしで動かすときのつなぎで、プロセスを再起動すると消える。
/// </summary>
public class InMemoryNewReleaseStore : INewReleaseStore
{
    readonly object _gate = new();
    readonly Dictionary<Uri, NewRelease> _byUrl = [];

    public Task<int> AddRangeAsync(
        IEnumerable<NewRelease> releases, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var added = 0;
            foreach (var release in releases)
            {
                // 既にある行は上書きする(同じ窓を毎回引き直す観測の表なので、
                // 正規化の規則を変えたら次の収集で揃ってほしい)
                if (!_byUrl.ContainsKey(release.Url))
                {
                    added++;
                }

                _byUrl[release.Url] = release;
            }

            return Task.FromResult(added);
        }
    }

    public Task<IReadOnlyList<NewRelease>> GetPublishedSinceAsync(
        DateOnly since, int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<NewRelease> result = _byUrl.Values
                .Where(release => release.PublishedOn is { } published && published >= since)
                .OrderByDescending(release => release.PublishedOn)
                .ThenByDescending(release => release.CollectedAt)
                .Take(count)
                .ToList();

            return Task.FromResult(result);
        }
    }
}
