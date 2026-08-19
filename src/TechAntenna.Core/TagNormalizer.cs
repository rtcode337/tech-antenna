using System.Text;

namespace TechAntenna.Core;

/// <summary>
/// 収集元ごとに異なるタグの表記をそろえ、突き合わせに使えるキーにする。
/// 記事・イベント・書籍を横断してタグで突き合わせるため、保存前に必ずここを通す。
///
/// ここで潰すのは機械的な表記ゆれだけ(大小文字・全角半角・区切り)。
/// 「ai と 人工知能」のような同義語は機械的には潰せないので、別名カタログの仕事にする。
/// 「ai ⊃ 生成ai ⊃ llm」は同義ではなく粒度の違いなので、そもそも統合しない
/// —— まとめると上位の語だけが巨大化して、何の話題か分からなくなる。
/// </summary>
public static class TagNormalizer
{
    /// <summary>
    /// トピックとして扱わないタグ。収集元が付ける分類名や、読み手の行動を表す語。
    ///
    /// 落とすのは「何の話題か」を表していないため。実データでの出現数(772 タグ中)は
    /// テクノロジー 60・あとで読む 49・初心者 37 で、上位 5 件のうち 2 件がこれだった。
    /// 残すと話題度の上位をこの手の語が占め、トピック一覧が使い物にならなくなる。
    /// </summary>
    static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        // はてなブックマークのカテゴリ・行動タグ
        "テクノロジー", "あとで読む", "これはすごい", "ネタ", "話題",
        // Qiita や Zenn で本文の性格を表すタグ
        "初心者", "初心者向け", "入門", "まとめ", "メモ", "備忘録", "tips", "新人プログラマ応援",
        "ポエム", "個人メモ", "学習記録",
    };

    /// <summary>
    /// 1つのタグの中に複数の語が入っているときの区切り。
    /// 収集元のタグ名にカンマが入っていることが実際にある(実測: Qiita の直近 100 記事の
    /// タグ 346 個のうち `SEOツール,`・`AI活用,`・`コスト重視,` の 3 個)。
    /// 落とすだけだと `a,b` が `ab` という別の語になってしまうので、ここで分ける。
    /// </summary>
    static readonly char[] TagSeparators = [',', '、'];

    /// <summary>タグの一覧を正規化する。空の値・ストップワード・重複は落とし、出現順は保つ。</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> tags) =>
        tags.SelectMany(tag => tag.Split(TagSeparators))
            .Select(ToKey)
            .Where(tag => tag.Length > 0 && !Stopwords.Contains(tag))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 語の飾りとして先頭・末尾に付くだけの記号。位置で扱いを変えるのが要点:
    /// 先頭の `#` はハッシュタグの印(実測: Qiita のタグに `#生成AI`・`#プログラミング` がある)で、
    /// 落とさないと `生成ai` と別のトピックに割れる。一方語の中の `#` は残す(`c#`)。
    /// `*` は Markdown の強調が漏れたもの(実測: `Video` というタグ名があった)。
    ///
    /// `.` は先頭でも落とさない —— `.net` が `net` になってしまう。
    /// </summary>
    static readonly char[] LeadingNoise = ['#', '*'];

    static readonly char[] TrailingNoise = ['*', '。', '.'];

    /// <summary>1つのタグを突き合わせキーに直す。</summary>
    public static string ToKey(string tag)
    {
        // NFKC で全角英数と半角カナをそろえる(「ＡＩ」と「AI」、「ｼﾞｪﾈﾚｰﾃｨﾌﾞ」と「ジェネレーティブ」)。
        // 収集元によって入力方法が違うため、これをしないと同じ語が別トピックに割れる。
        var folded = tag.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();

        var key = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            // 区切りは落として「claude code」と「claudecode」を同じキーにする。
            // 記号のうち `.` `#` `+` は残す —— 落とすと `.net` が `net` に、`c#` が `c` に、
            // `c++` が `c` になって、まったく別の語と衝突するため。
            if (c is ' ' or '　' or '-' or '_' or '・' or '/')
            {
                continue;
            }

            // 制御文字(改行・タブ等)も落とす。`Trim()` が消すのは前後だけなので、
            // 収集元のタグの途中に紛れ込むとキーに残り、そのまま DB・画面・ログへ流れる
            // (ログに改行が入ると、偽の行を差し込める = ログの偽装)。
            // ここで落とせば、キーを扱うすべての場所が同時に守られる
            if (char.IsControl(c))
            {
                continue;
            }

            key.Append(c);
        }

        return key.ToString().TrimStart(LeadingNoise).TrimEnd(TrailingNoise);
    }
}
