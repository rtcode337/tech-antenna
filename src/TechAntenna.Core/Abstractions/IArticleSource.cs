using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>記事の収集元(RSS / Atom フィード等)。</summary>
public interface IArticleSource
{
    string Name { get; }

    Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 論文の収集元(arXiv・J-STAGE)。**記事と分けてあるのは収集の起こし方が違うから** ——
/// 記事の RSS は巡回だが、論文は<b>検索</b>なので収集対象に選んだトピックが要る。
/// 同じボタンにまとめると「記事は集まったのに論文は 0 件」の理由が画面から分からない。
/// </summary>
public interface IPaperSource : IArticleSource;
