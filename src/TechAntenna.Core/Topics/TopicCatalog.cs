namespace TechAntenna.Core.Topics;

/// <summary>
/// カタログに載せる1トピック。
/// </summary>
/// <param name="Display">画面に出す正式表記(`生成AI`)。検索語として外部 API へ投げるのもこれ。</param>
/// <param name="Aliases">同じものを指す別の書き方(`人工知能`・`generative ai`)。突き合わせのときに正式表記へ寄せる。</param>
/// <param name="Parent">1つ上の粒度(`LLM` の親は `生成AI`)。**統合はしない** —— まとめると上位の語だけが巨大化するため。</param>
/// <param name="Description">
/// 用語の一言説明(1〜2文)。見慣れない語が一覧に並んだときに、開かなくても何の話か分かるように持つ。
/// JSON に書けば人の記述が使われ、無ければ LLM が再編成のときに埋める。
/// </param>
/// <param name="English">
/// 英語圏の収集元へ投げる検索語(`generative ai`)。arXiv に日本語の正式表記を投げると
/// 0 件になるため別に持つ。無ければ正式表記を使う。
/// </param>
public record TopicCatalogEntry(
    string Display,
    IReadOnlyList<string> Aliases,
    string? Parent,
    string? Description = null,
    string? English = null)
{
    /// <summary>突き合わせに使うキー。正式表記を機械的に正規化したもの。</summary>
    public string Key => TagNormalizer.ToKey(Display);
}

/// <summary>
/// トピックの語彙と別名の対応表。**読み取り用のスナップショット**で、
/// 権威は DB(<see cref="Topic"/> と <see cref="Tag"/>)にある ——
/// 起動時と再編成のあとに <see cref="Replace"/> で組み直す。
///
/// <see cref="TagNormalizer"/> が潰すのは機械的な表記ゆれだけなので、
/// 「ai と 人工知能」のような**同義語をまとめるのはこちらの仕事**
/// (別名は <see cref="TagStatus.Alias"/> のタグから組む)。
///
/// インスタンスは DI で各収集ソースに配られた後も同じ参照のまま差し替わる
/// (中身を不変のスナップショットとして丸ごと入れ替えるので、読む側のロックは不要)。
/// </summary>
public class TopicCatalog
{
    /// <summary>カタログが読めない・空のときに使う。別名解決をしないだけで、正規化は動く。</summary>
    public static TopicCatalog Empty => new([]);

    /// <summary>読み取りが常に一貫するよう、中身は不変のまとまりで丸ごと差し替える。</summary>
    sealed record Snapshot(
        IReadOnlyList<TopicCatalogEntry> Entries,
        Dictionary<string, TopicCatalogEntry> ByKey,
        Dictionary<string, string> AliasToKey,
        ILookup<string, TopicCatalogEntry> ChildrenByParent);

    volatile Snapshot _snapshot;

    public TopicCatalog(IReadOnlyList<TopicCatalogEntry> entries) => _snapshot = Build(entries);

    static Snapshot Build(IReadOnlyList<TopicCatalogEntry> entries)
    {
        // 同じキーの後勝ちはさせない(呼ぶ側が並べた優先順を尊重する)
        var byKey = new Dictionary<string, TopicCatalogEntry>(StringComparer.Ordinal);
        var kept = new List<TopicCatalogEntry>();
        foreach (var entry in entries)
        {
            if (byKey.TryAdd(entry.Key, entry))
            {
                kept.Add(entry);
            }
        }

        // 別名 → 正式表記のキー。別名どうしが衝突したら先に書いてあるほうを採る
        var aliasToKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in kept)
        {
            foreach (var alias in entry.Aliases)
            {
                aliasToKey.TryAdd(TagNormalizer.ToKey(alias), entry.Key);
            }
        }

        // 親のキー → 子。トピック詳細のツリーで使う(引くたびに全件を走らせずに済む)
        var childrenByParent = kept
            .Select(entry => (Parent: ParentKeyOf(entry, aliasToKey), Entry: entry))
            .Where(pair => pair.Parent is { Length: > 0 } parent && parent != pair.Entry.Key)
            .ToLookup(pair => pair.Parent!, pair => pair.Entry, StringComparer.Ordinal);

        return new Snapshot(kept, byKey, aliasToKey, childrenByParent);
    }

    /// <summary>
    /// エントリの親を突き合わせキーに直す。**別名で書かれていても正式表記へ寄せる** ——
    /// 寄せないと(JSON に <c>parent: 人工知能</c> と書いたときなど)実在しないキーを指し、
    /// ツリーで親を見失って根として孤立する。
    /// </summary>
    static string? ParentKeyOf(TopicCatalogEntry entry, Dictionary<string, string> aliasToKey)
    {
        if (entry.Parent is not { Length: > 0 } parent)
        {
            return null;
        }

        var key = TagNormalizer.ToKey(parent);

        return aliasToKey.GetValueOrDefault(key, key);
    }

    public IReadOnlyList<TopicCatalogEntry> Entries => _snapshot.Entries;

    /// <summary>
    /// キーが<b>トピック本体</b>(カタログの正式表記)か。**別名は含まない** ——
    /// 一覧に出すのは正式表記の行だけで、別名の行は正式表記に吸収された重複だから
    /// (<see cref="Contains"/> は別名も true になるので、この用途には使えない)。
    /// </summary>
    public bool IsTopic(string key) => _snapshot.ByKey.ContainsKey(key);

    /// <summary>キーがカタログに載っているか(正式表記・別名のどちらでも)。</summary>
    public bool Contains(string key)
    {
        var snapshot = _snapshot;
        return snapshot.ByKey.ContainsKey(key) || snapshot.AliasToKey.ContainsKey(key);
    }

    /// <summary>
    /// 中身を丸ごと差し替える。**語彙の権威は DB 側**にあり、ここは読み取り用の
    /// スナップショット —— 起動時と再編成のあとに、DB から組み直して入れ替える。
    /// (以前は JSON に LLM の分類を合成していたが、DB を実体にしたので合成は要らなくなった)
    /// </summary>
    public void Replace(IReadOnlyList<TopicCatalogEntry> entries) => _snapshot = Build(entries);

    /// <summary>
    /// キーに登録されている別名。カタログに無いキーは空。
    /// **検索で「類義語も含めて当てる」ために要る** —— `人工知能` で引いて `AI` に当てたい。
    /// </summary>
    public IReadOnlyList<string> AliasesOf(string key) =>
        _snapshot.ByKey.TryGetValue(key, out var entry) ? entry.Aliases : [];

    /// <summary>キーに対する一言説明。無ければ null。</summary>
    public string? DescriptionOf(string key) =>
        _snapshot.ByKey.TryGetValue(key, out var entry)
            && entry.Description is { Length: > 0 } description
            ? description
            : null;

    /// <summary>
    /// タグ1つを、カタログの正式表記のキーに寄せる。
    /// **カタログに無い語は落とさず、機械的に正規化しただけの値を返す** ——
    /// 落とすと新しいトピックが永久に入ってこなくなるため。
    /// </summary>
    public string Resolve(string tag)
    {
        var key = TagNormalizer.ToKey(tag);

        return _snapshot.AliasToKey.GetValueOrDefault(key, key);
    }

    /// <summary>タグの一覧を正規化し、別名を正式表記へ寄せる。空・ストップワード・重複は落とす。</summary>
    public IReadOnlyList<string> Normalize(IEnumerable<string> tags)
    {
        var snapshot = _snapshot;

        return TagNormalizer.Normalize(tags)
            .Select(tag => snapshot.AliasToKey.GetValueOrDefault(tag, tag))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

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
        _snapshot.ByKey.TryGetValue(key, out var entry) ? entry.Display : key;

    /// <summary>
    /// 英語圏の収集元へ投げる検索語。**英語表記があればそれを、無ければ ASCII だけの別名、
    /// それも無ければ正式表記**を返す(`生成AI` → `generative ai`)。
    ///
    /// arXiv のような英語の収集元に日本語の正式表記をそのまま投げると 0 件になる
    /// —— 実測で `生成AI` は 0 件だった。
    /// </summary>
    public string EnglishTermOf(string key) =>
        _snapshot.ByKey.TryGetValue(key, out var entry)
            ? entry.English is { Length: > 0 } english
                ? english
                : entry.Aliases.FirstOrDefault(alias => alias.All(char.IsAscii)) ?? entry.Display
            : key;

    /// <summary>キーに対する1つ上の粒度。無ければ null。</summary>
    public string? ParentOf(string key)
    {
        var snapshot = _snapshot;

        return snapshot.ByKey.TryGetValue(key, out var entry)
            ? ParentKeyOf(entry, snapshot.AliasToKey)
            : null;
    }

    /// <summary>
    /// トピック1件の<b>語彙としての姿</b>(同義語と親子ツリー上の位置)を組む。トピック詳細で使う。
    ///
    /// 引数は正式表記のキーでも別名でも、正規化前の表記でもよい(内部で寄せる)。
    /// **カタログに無い語でも null は返さない** —— まだ分類されていないタグ(平置きの語)にも
    /// 詳細ページはあるので、同義語も親子も空の姿を返して「載っていない」と示す。
    /// </summary>
    public TopicStructure StructureOf(string tag)
    {
        var snapshot = _snapshot;
        var key = TagNormalizer.ToKey(tag);
        key = snapshot.AliasToKey.GetValueOrDefault(key, key);

        if (!snapshot.ByKey.TryGetValue(key, out var entry))
        {
            return new TopicStructure(key, key, InCatalog: false, Description: null, [], [], []);
        }

        // 同じ語を二度出さない。**LLM の分類は人手で検証されない**ので、親子が万一
        // 循環していてもここで打ち切る(ツリーを組む側で無限に潜らないようにする)
        var visited = new HashSet<string>(StringComparer.Ordinal) { entry.Key };

        var ancestors = new List<TopicName>();
        var current = entry;
        while (ParentKeyOf(current, snapshot.AliasToKey) is { } parentKey
            && visited.Add(parentKey)
            && snapshot.ByKey.TryGetValue(parentKey, out var parent))
        {
            ancestors.Add(new TopicName(parent.Key, parent.Display));
            current = parent;
        }

        // 辿った順は「直近の親 → 根」なので、画面の描画順(上から)に合わせて反転する
        ancestors.Reverse();

        return new TopicStructure(
            entry.Key,
            entry.Display,
            InCatalog: true,
            entry.Description,
            entry.Aliases,
            ancestors,
            BuildChildren(snapshot, entry.Key, visited));
    }

    /// <summary>
    /// そのトピックの配下(子・孫…)のキーを全部返す。自分自身は含めない。
    /// カタログに無い語・葉のトピックは空。
    /// </summary>
    public IReadOnlyList<string> DescendantKeysOf(string tag)
    {
        var keys = new List<string>();
        Walk(StructureOf(tag).Children);

        void Walk(IReadOnlyList<TopicTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                keys.Add(node.Key);
                Walk(node.Children);
            }
        }

        return keys;
    }

    /// <summary>
    /// 選択されたトピックに<b>その配下すべて</b>を足したキーの一覧を返す。
    ///
    /// **親を選んだら子も収集対象にする**ため —— 「AI を集めたい」と言っているのに
    /// `LLM` や `RAG` のイベント・書籍が集まらないのは、選んだ側の期待と合わない。
    /// (イベントと書籍はトピック 1 つごとに外部へ問い合わせるので、
    /// 大きな親を選ぶとリクエスト数もその配下のぶんだけ増える)
    /// </summary>
    public IReadOnlyList<string> ExpandWithDescendants(IEnumerable<string> tags)
    {
        var expanded = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            var key = Resolve(tag);
            if (seen.Add(key))
            {
                expanded.Add(key);
            }

            foreach (var descendant in DescendantKeysOf(key))
            {
                if (seen.Add(descendant))
                {
                    expanded.Add(descendant);
                }
            }
        }

        return expanded;
    }

    /// <summary>配下のツリーを組む。並びは正式表記順(話題度は語彙側では持たないため)。</summary>
    static IReadOnlyList<TopicTreeNode> BuildChildren(
        Snapshot snapshot,
        string key,
        HashSet<string> visited) =>
        snapshot.ChildrenByParent[key]
            .Where(child => visited.Add(child.Key))
            .OrderBy(child => child.Display, StringComparer.Ordinal)
            .Select(child => new TopicTreeNode(
                child.Key, child.Display, BuildChildren(snapshot, child.Key, visited)))
            .ToList();
}
