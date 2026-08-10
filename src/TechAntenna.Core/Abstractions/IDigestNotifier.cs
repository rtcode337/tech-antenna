using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// 生成したダイジェスト(今日のサマリー)を外部へ通知する。
/// 実装は ntfy(<c>NtfyDigestNotifier</c>)。通知先は画面から実行時に設定できるため
/// 常に登録され、未設定・無効のときは送らずに false を返す。
/// </summary>
public interface IDigestNotifier
{
    /// <summary>通知先の名前(結果の文言とログに出す)。</summary>
    string Name { get; }

    /// <summary>
    /// ダイジェストを1件通知する。実際に送ったら true、未設定・無効でスキップしたら false。
    /// 失敗は例外(呼び出し側がログに落とし、生成自体は成功のまま)。
    /// </summary>
    Task<bool> NotifyAsync(Digest digest, CancellationToken cancellationToken = default);
}
