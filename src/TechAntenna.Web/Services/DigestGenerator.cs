using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>
/// 今日のサマリーを書かせる相手 1 つ。**複数の AI で同じ材料から書かせて読み比べる**ため、
/// 誰が書いたか(<paramref name="Key"/>)と、メインかどうかを一緒に持つ。
/// </summary>
/// <param name="Key">突き合わせのキー(`chiezo:gemini` / `default`)。表示名と別に持つのは、
/// 表示名がモデル名まで含んで変わりうるため。</param>
/// <param name="Name">画面に出す名前(モデルまで含む)。</param>
/// <param name="IsPrimary">メインの相手か。通知とホームの既定の表示に使う。</param>
/// <param name="Composer">生成の実体。</param>
public record DigestGenerator(string Key, string Name, bool IsPrimary, IDigestComposer Composer);
