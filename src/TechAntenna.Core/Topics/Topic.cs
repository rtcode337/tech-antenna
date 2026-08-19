namespace TechAntenna.Core.Topics;

/// <summary>
/// 語彙としてのトピック1件。タグ(<see cref="Tag"/>)から精査で昇格したものだけがここに入る。
///
/// 権威は DB 側にある。`topic-seed.json` は<b>DB が空のときに流し込むシード</b>で、
/// 以後の衝突ルールは持たない(手直しは画面から状態を書き換える)。
/// </summary>
public class Topic
{
    /// <summary>正規化済みキー。主キー。対応する <see cref="Tag"/> と同じ値。</summary>
    public string Key { get; set; } = "";

    /// <summary>画面に出す正式表記(`生成AI`)。外部 API へ投げる検索語にも使う。</summary>
    public string Display { get; set; } = "";

    /// <summary>1つ上の粒度のキー(`llm` の親は `生成ai`)。最上位なら null。</summary>
    public string? Parent { get; set; }

    /// <summary>
    /// 英語圏の収集元へ投げる検索語(`generative ai`)。
    /// arXiv に日本語の正式表記を投げると 0 件になるため別に持つ。無ければ正式表記を使う。
    /// </summary>
    public string? English { get; set; }

    /// <summary>用語の一言説明(1〜2文)。無ければ null。</summary>
    public string? Description { get; set; }

    /// <summary>この語彙の出どころ(シード / LLM / 人の手直し)。</summary>
    public DecidedBy DecidedBy { get; set; }

    /// <summary>このアプリの収集キーワードとして選択されているか。</summary>
    public bool IsSelected { get; set; }

    /// <summary>この語単体の話題度(配下は含まない)。</summary>
    public double TrendScore { get; set; }

    /// <summary>
    /// 配下を含めた話題度(自身 + 子孫の合計)。「プログラミング言語」のような構造の語は
    /// 単体の話題度がほぼ付かないので、ツリーの並びにはこちらを使う。
    /// </summary>
    public double SubtreeTrendScore { get; set; }

    /// <summary>紐づくタグ(自分自身 + 別名)に付いている件数の合計。</summary>
    public int ArticleCount { get; set; }

    public int EventCount { get; set; }

    public int BookCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>記事・イベント・書籍のうち何種類がそろっているか(0〜3)。</summary>
    public int Coverage =>
        (ArticleCount > 0 ? 1 : 0) + (EventCount > 0 ? 1 : 0) + (BookCount > 0 ? 1 : 0);

    public int TotalCount => ArticleCount + EventCount + BookCount;
}
