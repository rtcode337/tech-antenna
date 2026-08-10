using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>
/// メモリ上のシークレットストア。DB 接続なしで動かすときのつなぎで、
/// 画面から設定したキーはプロセスを再起動すると消える(環境変数のフォールバックは残る)。
/// </summary>
public class InMemorySecretStore(TimeProvider timeProvider) : ISecretStore
{
    readonly object _gate = new();
    readonly Dictionary<string, Secret> _secrets = [];

    public Task<IReadOnlyList<Secret>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Secret>>([.. _secrets.Values]);
        }
    }

    public Task SetAsync(
        string name, string protectedValue, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _secrets[name] = new Secret
            {
                Name = name,
                Value = protectedValue,
                UpdatedAt = timeProvider.GetUtcNow(),
            };
            return Task.CompletedTask;
        }
    }

    public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _secrets.Remove(name);
            return Task.CompletedTask;
        }
    }
}
