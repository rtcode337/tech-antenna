namespace TechAntenna.Core.Topics;

/// <summary>
/// カタログに載せる1トピック。
/// </summary>
/// <param name="Display">画面に出す正式表記(`生成AI`)。検索語として外部 API へ投げるのもこれ。</param>
/// <param name="Aliases">同じものを指す別の書き方(`人工知能`・`generative ai`)。突き合わせのときに正式表記へ寄せる。</param>
/// <param name="Parent">1つ上の粒度(`LLM` の親は `生成AI`)。**統合はしない** —— まとめると上位の語だけが巨大化するため。</param>
public record TopicCatalogEntry(string Display, IReadOnlyList<string> Aliases, string? Parent)
{
    /// <summary>突き合わせに使うキー。正式表記を機械的に正規化したもの。</summary>
    public string Key => TagNormalizer.ToKey(Display);
}

/// <summary>
/// トピックの語彙と別名の対応表。
///
/// <see cref="TagNormalizer"/> が潰すのは機械的な表記ゆれだけなので、
/// 「ai と 人工知能」のような**同義語をまとめるのはこちらの仕事**。
/// 中身はコードではなくデータ(`topic-catalog.json`)として持ち、人が直せるようにしている。
/// </summary>
public class TopicCatalog
{
    /// <summary>カタログが読めない・空のときに使う。別名解決をしないだけで、正規化は動く。</summary>
    public static readonly TopicCatalog Empty = new([]);

    readonly Dictionary<string, TopicCatalogEntry> _byKey;
    readonly Dictionary<string, string> _aliasToKey;

    public TopicCatalog(IReadOnlyList<TopicCatalogEntry> entries)
    {
        Entries = entries;
        _byKey = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);

        // 別名 → 正式表記のキー。別名どうしが衝突したら先に書いてあるほうを採る
        _aliasToKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var alias in entry.Aliases)
            {
                _aliasToKey.TryAdd(TagNormalizer.ToKey(alias), entry.Key);
            }
        }
    }

    public IReadOnlyList<TopicCatalogEntry> Entries { get; }

    /// <summary>
    /// タグ1つを、カタログの正式表記のキーに寄せる。
    /// **カタログに無い語は落とさず、機械的に正規化しただけの値を返す** ——
    /// 落とすと新しいトピックが永久に入ってこなくなるため。
    /// </summary>
    public string Resolve(string tag)
    {
        var key = TagNormalizer.ToKey(tag);

        return _aliasToKey.GetValueOrDefault(key, key);
    }

    /// <summary>タグの一覧を正規化し、別名を正式表記へ寄せる。空・ストップワード・重複は落とす。</summary>
    public IReadOnlyList<string> Normalize(IEnumerable<string> tags) =>
        TagNormalizer.Normalize(tags)
            .Select(tag => _aliasToKey.GetValueOrDefault(tag, tag))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>キーに対する画面表示用の表記。カタログに無ければキーをそのまま返す。</summary>
    public string DisplayOf(string key) =>
        _byKey.TryGetValue(key, out var entry) ? entry.Display : key;

    /// <summary>キーに対する1つ上の粒度。無ければ null。</summary>
    public string? ParentOf(string key) =>
        _byKey.TryGetValue(key, out var entry) && entry.Parent is { Length: > 0 } parent
            ? TagNormalizer.ToKey(parent)
            : null;
}
