namespace TechAntenna.Core.Models;

/// <summary>収集した技術記事。</summary>
public class Article
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    /// <summary>記事の URL。重複判定のキーとして使う。</summary>
    public required Uri Url { get; init; }

    /// <summary>収集元の名前(例: Zenn、Qiita)。</summary>
    public required string SourceName { get; init; }

    /// <summary>種別(記事 / ニュース / 論文)。一覧を分けるのに使う。</summary>
    public ArticleKind Kind { get; init; } = ArticleKind.Article;

    /// <summary>フィードが提供する本文の抜粋(HTML 除去済み)。要約の材料に使う。</summary>
    public string? ContentSnippet { get; init; }

    /// <summary>LLM による要約。未生成の間は null。</summary>
    public string? Summary { get; set; }

    /// <summary>
    /// LLM による日本語の訳題(英語の論文タイトル用)。未処理の間は null、
    /// **訳す必要が無いと判断したものは空文字**(毎回訳しに行かないための確定)。
    /// 原題(<see cref="Title"/>)は消さずに併記する —— 無いと他の文献と突き合わせられない。
    /// </summary>
    public string? TitleJa { get; set; }

    /// <summary>収集元が公開日時を提供しない場合は null。</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    public required DateTimeOffset CollectedAt { get; init; }

    /// <summary>
    /// 正規化済みのタグ(<see cref="TagNormalizer"/> を通した値)。突き合わせに使う。
    /// init ではなく set なのは、正規化の規則を変えたときに <c>RawTags</c> から作り直すため。
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>
    /// 収集元から受け取ったままのタグ。**正規化の規則を変えたら、ここから引き直す**。
    /// 正規化後の値しか持たないと、別名カタログを直しても過去のデータに反映できない
    /// (`claude code` を `claudecode` に寄せた後で分けたくなっても、元の表記が残っていない)。
    /// </summary>
    public IReadOnlyList<string> RawTags { get; init; } = [];
}
