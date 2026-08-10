using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// 画面から設定した API キー・トークンの置き場。値の暗号化・復号は呼び出し側
/// (Web 層の ApiCredentials)の仕事で、ストアは保護済みの文字列を預かるだけ。
/// </summary>
public interface ISecretStore
{
    Task<IReadOnlyList<Secret>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SetAsync(string name, string protectedValue, CancellationToken cancellationToken = default);

    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
}
