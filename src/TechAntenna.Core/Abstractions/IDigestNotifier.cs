using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// 生成したダイジェスト(今日のサマリー)を外部へ通知する。
/// 実装は ntfy(<c>NtfyDigestNotifier</c>)。未設定なら DI に登録されず、通知なしで動く。
/// </summary>
public interface IDigestNotifier
{
    /// <summary>通知先の名前(結果の文言とログに出す)。</summary>
    string Name { get; }

    /// <summary>ダイジェストを1件通知する。失敗は例外(呼び出し側がログに落とし、生成自体は成功のまま)。</summary>
    Task NotifyAsync(Digest digest, CancellationToken cancellationToken = default);
}
