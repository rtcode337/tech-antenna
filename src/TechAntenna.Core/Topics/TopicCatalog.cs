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

    /// <summary>
    /// テキストに出てくるトピックを、カタログの**正式表記**で返す(`RawTags` にそのまま入れられる)。
    ///
    /// **フィードがタグを持たない収集元のためのタグ付け。** Zenn の RSS も Qiita の Atom も
    /// `category` 要素を持たず、ニュースサイトも同様なので、収集元のタグだけに頼ると
    /// タグが空のまま保存され、トピック横断にも一覧の強調にも乗らない。
    ///
    /// 渡すのは**タイトルだけ**にすること。本文まで見ると、文中で一度触れただけの語で
    /// タグが付いてしまう(Doorkeeper の `q` が説明文に当たって意味を失ったのと同じ)。
    /// 判定は <see cref="KeywordMatcher"/> なので、`AI` が `Rails`・`email` には当たらない。
    /// </summary>
    public IReadOnlyList<string> FindIn(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : Entries
                .Where(entry => entry.Aliases.Prepend(entry.Display)
                    .Any(term => KeywordMatcher.Contains(text, term)))
                .Select(entry => entry.Display)
                .ToList();

    /// <summary>キーに対する画面表示用の表記。カタログに無ければキーをそのまま返す。</summary>
    public string DisplayOf(string key) =>
        _byKey.TryGetValue(key, out var entry) ? entry.Display : key;

    /// <summary>
    /// 英語圏の収集元へ投げる検索語。**ASCII だけでできた別名があればそれを、無ければ正式表記**を返す
    /// (`生成AI` → `generative ai`、`機械学習` → `machine learning`)。
    ///
    /// arXiv のような英語の収集元に日本語の正式表記をそのまま投げると 0 件になる —— 実測で
    /// `生成AI` は 0 件だった。別名カタログに英語表記を持たせてあるので、そこから拾う。
    /// ASCII の別名が無いトピックは日本語のまま投げることになる(その収集元では当たらない)。
    /// </summary>
    public string EnglishTermOf(string key) =>
        _byKey.TryGetValue(key, out var entry)
            ? entry.Aliases.FirstOrDefault(alias => alias.All(char.IsAscii)) ?? entry.Display
            : key;

    /// <summary>キーに対する1つ上の粒度。無ければ null。</summary>
    public string? ParentOf(string key) =>
        _byKey.TryGetValue(key, out var entry) && entry.Parent is { Length: > 0 } parent
            ? TagNormalizer.ToKey(parent)
            : null;
}
