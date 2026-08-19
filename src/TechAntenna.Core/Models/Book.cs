using TechAntenna.Core.Abstractions;

namespace TechAntenna.Core.Models;

/// <summary>書籍の書誌情報。</summary>
public class Book
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    /// <summary>ISBN-13(ハイフンなし)。提供されないソースでは null。</summary>
    public string? Isbn13 { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public string? Publisher { get; init; }

    public DateOnly? PublishedOn { get; init; }

    /// <summary>書誌詳細ページの URL。</summary>
    public Uri? Url { get; init; }

    /// <summary>
    /// 書影の URL。画像そのものは保持せずリンクのみを持つ。
    ///
    /// init ではなく set なのは、後から埋まることがあるため(<see cref="BookMerge"/>)。
    /// 定番の書籍は ISBN から組み立てるので、書影の補完を足す前に保存した本は
    /// 書影が null のまま残っている —— 合流のときに埋められないと、次の収集で
    /// 取り直しても保存されず、いつまでも表紙の出ない一覧になる。
    /// </summary>
    public Uri? CoverUrl { get; set; }

    /// <summary>収集元の名前(例: openBD、Google Books)。</summary>
    public required string SourceName { get; init; }

    /// <summary>
    /// レビュー件数。「どのくらい読まれているか」の代理指標で、定番書ほど積み上がる。
    /// 取得元(楽天ブックス)を設定していない・その本が見つからないときは null
    /// (「0 件」とは意味が違う —— 0 は読まれていない、null は分からない)。
    /// </summary>
    public int? ReviewCount { get; set; }

    /// <summary>平均評価(5点満点)。レビューが無ければ null。</summary>
    public double? ReviewAverage { get; set; }

    /// <summary>
    /// この本を薦めていた記事(出典。URL と題名)。レビュー数とは別軸の指標で、
    /// レビューが「どれだけ読まれたか」なら、こちらは「詳しい人が薦めたか」。
    /// </summary>
    public IReadOnlyList<RecommendedArticle> RecommendedBy { get; set; } = [];

    /// <summary>何本の記事で薦められたか。<see cref="RecommendedBy"/> から導くので列は持たない。</summary>
    public int RecommendationCount => RecommendedBy.Count;

    /// <summary>
    /// 読み終えた日時。未読は null(画面から立てる)。
    ///
    /// 外から取れる指標(<see cref="ReviewCount"/>・<see cref="RecommendedBy"/>)とは別の軸。
    /// あちらは「世の中でどれだけ読まれ、薦められているか」で、こちらは本人しか持てない情報
    /// —— 混ぜると「読まれている本」と「自分が読んだ本」の区別が付かなくなる。
    ///
    /// 日時で持つのは、いつ読んだかを画面に出せるようにするため(真偽値だと後から足せない)。
    /// init ではなく set なのは後から立てるからで、収集は決してここを触らない
    /// (<see cref="BookMerge"/>)—— 触ると再収集のたびに読んだ印が消える。
    /// </summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>読んだ本か。<see cref="ReadAt"/> から導くので列は持たない。</summary>
    public bool IsRead => ReadAt is not null;

    public required DateTimeOffset CollectedAt { get; init; }

    /// <summary>
    /// 正規化済みのタグ(<see cref="TagNormalizer"/> を通した値)。突き合わせに使う。
    /// init ではなく set なのは、正規化の規則を変えたときに <c>RawTags</c> から作り直すため。
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>
    /// 収集元から受け取ったままのタグ。正規化の規則を変えたら、ここから引き直す。
    /// 正規化後の値しか持たないと、別名カタログを直しても過去のデータに反映できない
    /// (`claude code` を `claudecode` に寄せた後で分けたくなっても、元の表記が残っていない)。
    ///
    /// 記事・イベントと違って init ではなく set なのは、同じ本が別のトピックでも見つかったときに
    /// タグを足すため(<see cref="BookMerge"/>)。ここを足し忘れると、再正規化した瞬間に
    /// 後から足したトピックのタグだけが消える。
    /// </summary>
    public IReadOnlyList<string> RawTags { get; set; } = [];
}
