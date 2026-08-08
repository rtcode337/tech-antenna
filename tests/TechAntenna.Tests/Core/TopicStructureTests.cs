using TechAntenna.Core.Topics;

namespace TechAntenna.Tests.Core;

/// <summary>トピック詳細に出す「語彙としての姿」(同義語と親子ツリー)の組み立て。</summary>
public class TopicStructureTests
{
    static TopicCatalog Catalog() => new(
    [
        new TopicCatalogEntry("AI", ["人工知能", "artificial intelligence"], null),
        new TopicCatalogEntry("生成AI", ["generative ai"], "AI"),
        new TopicCatalogEntry("LLM", ["大規模言語モデル"], "生成AI"),
        new TopicCatalogEntry("RAG", [], "生成AI"),
        new TopicCatalogEntry("機械学習", ["machine learning"], "AI"),
    ]);

    [Fact]
    public void 同義語と祖先と子孫を返す()
    {
        var structure = Catalog().StructureOf("生成ai");

        Assert.True(structure.InCatalog);
        Assert.Equal("生成AI", structure.Display);
        Assert.Equal(["generative ai"], structure.Aliases);
        // 祖先は根 → 直近の親の順(画面はツリーを上から描く)
        Assert.Equal(["ai"], structure.Ancestors.Select(a => a.Key));
        Assert.Equal(["llm", "rag"], structure.Children.Select(c => c.Key));
        Assert.False(structure.IsIsolated);
    }

    [Fact]
    public void 別名や正規化前の表記で引いても正式表記に寄せる()
    {
        // 画面のリンクはキーで張るが、URL を手で叩かれることもある
        var catalog = Catalog();

        Assert.Equal("ai", catalog.StructureOf("人工知能").Key);
        Assert.Equal("AI", catalog.StructureOf("Artificial Intelligence").Display);
        Assert.Equal("llm", catalog.StructureOf("大規模言語モデル").Key);
    }

    [Fact]
    public void 孫まで潜って階層を保つ()
    {
        var structure = Catalog().StructureOf("ai");

        // 並びは正式表記の順(語彙側は話題度を持たないため)
        Assert.Equal(["機械学習", "生成AI"], structure.Children.Select(c => c.Display));
        var generative = structure.Children.First(c => c.Key == "生成ai");
        Assert.Equal(["llm", "rag"], generative.Children.Select(c => c.Key));
        Assert.Empty(structure.Ancestors);
    }

    [Fact]
    public void カタログに無い語でも姿を返す()
    {
        // 平置きの語(まだ分類されていないタグ)にも詳細ページはある
        var structure = Catalog().StructureOf("わからない語");

        Assert.False(structure.InCatalog);
        Assert.Equal("わからない語", structure.Key);
        Assert.Equal("わからない語", structure.Display);
        Assert.Empty(structure.Aliases);
        Assert.True(structure.IsIsolated);
    }

    [Fact]
    public void 親が別名で書かれていてもツリーがつながる()
    {
        // parent に別名(人工知能)を書くと、寄せない実装では実在しないキーを指して孤立する
        var catalog = new TopicCatalog(
        [
            new TopicCatalogEntry("AI", ["人工知能"], null),
            new TopicCatalogEntry("機械学習", [], "人工知能"),
        ]);

        Assert.Equal("ai", catalog.ParentOf("機械学習"));
        Assert.Equal(["ai"], catalog.StructureOf("機械学習").Ancestors.Select(a => a.Key));
        Assert.Equal(["機械学習"], catalog.StructureOf("ai").Children.Select(c => c.Key));
    }

    [Fact]
    public void 親子が循環していても止まる()
    {
        // LLM の分類は人手で検証されないので、循環しても画面が固まらないことを担保する
        var catalog = new TopicCatalog(
        [
            new TopicCatalogEntry("A", [], "B"),
            new TopicCatalogEntry("B", [], "A"),
        ]);

        var structure = catalog.StructureOf("a");

        Assert.Equal(["b"], structure.Ancestors.Select(ancestor => ancestor.Key));
        Assert.Empty(structure.Children);
    }

    [Fact]
    public void 配下のキーを全部返す()
    {
        var catalog = Catalog();

        Assert.Equal(["llm", "rag"], catalog.DescendantKeysOf("生成ai"));
        // 孫まで含める(選択を配下へ広げるのに使うので、直下だけでは足りない)
        Assert.Equal(["機械学習", "生成ai", "llm", "rag"], catalog.DescendantKeysOf("ai"));
        Assert.Empty(catalog.DescendantKeysOf("rag"));
        Assert.Empty(catalog.DescendantKeysOf("わからない語"));
    }

    [Fact]
    public void 選択を配下へ広げる()
    {
        // 親を選んだら子も収集対象にする(「AI を集めたい」のに RAG が集まらないのは期待と合わない)
        var catalog = Catalog();

        var expanded = catalog.ExpandWithDescendants(["生成AI"]);

        Assert.Equal(["生成ai", "llm", "rag"], expanded);
        // 別名で渡しても正式表記のキーに寄せる。重複は落とす
        Assert.Equal(
            ["ai", "機械学習", "生成ai", "llm", "rag"],
            catalog.ExpandWithDescendants(["人工知能", "LLM"]));
        // カタログに無い語はそのまま(配下は無い)
        Assert.Equal(["わからない語"], catalog.ExpandWithDescendants(["わからない語"]));
        Assert.Empty(catalog.ExpandWithDescendants([]));
    }

    [Fact]
    public void 説明はエントリから読む()
    {
        var catalog = new TopicCatalog(
        [
            new TopicCatalogEntry("AI", [], null, "人間の知的な作業をさせる技術の総称"),
            new TopicCatalogEntry("RAG", [], null),
        ]);

        Assert.Equal("人間の知的な作業をさせる技術の総称", catalog.DescriptionOf("ai"));
        Assert.Equal("人間の知的な作業をさせる技術の総称", catalog.StructureOf("ai").Description);
        Assert.Null(catalog.DescriptionOf("rag"));
        Assert.Null(catalog.DescriptionOf("わからない語"));
    }

    [Fact]
    public void 自分自身を親にしていても止まる()
    {
        var catalog = new TopicCatalog([new TopicCatalogEntry("A", [], "A")]);

        var structure = catalog.StructureOf("a");

        Assert.Empty(structure.Ancestors);
        Assert.Empty(structure.Children);
        Assert.True(structure.IsIsolated);
    }
}
