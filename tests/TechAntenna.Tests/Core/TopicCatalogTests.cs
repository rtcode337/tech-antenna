using TechAntenna.Core.Topics;

namespace TechAntenna.Tests.Core;

public class TopicCatalogTests
{
    static TopicCatalog Catalog() => new(
    [
        new TopicCatalogEntry("AI", ["人工知能", "artificial intelligence"], null),
        new TopicCatalogEntry("生成AI", ["ジェネレーティブAI", "generative ai"], "AI"),
        new TopicCatalogEntry("LLM", ["大規模言語モデル"], "生成AI"),
    ]);

    [Fact]
    public void タイトルからトピックを見つける()
    {
        // Zenn の RSS も Qiita の Atom も category を持たないので、これが無いとタグが空になる
        var catalog = Catalog();

        Assert.Equal(["AI", "生成AI"], catalog.FindIn("生成AIで社内文書を検索する"));
        // 別名で書かれていても正式表記で返す(RawTags にそのまま入れられる)
        Assert.Equal(["LLM"], catalog.FindIn("大規模言語モデルの評価手法"));
    }

    [Fact]
    public void 英数字に埋もれた語では誤爆しない()
    {
        // 単純な部分一致だと「AI」が「Rails」「email」に当たる
        var catalog = Catalog();

        Assert.Empty(catalog.FindIn("Rails 8 のもくもく会に参加した"));
        Assert.Empty(catalog.FindIn("email の配信基盤を作り直した"));
        Assert.Empty(catalog.FindIn(""));
        Assert.Empty(catalog.FindIn(null));
    }

    [Fact]
    public void 別名を正式表記のキーに寄せる()
    {
        var catalog = Catalog();

        Assert.Equal("ai", catalog.Resolve("人工知能"));
        Assert.Equal("ai", catalog.Resolve("Artificial Intelligence"));
        Assert.Equal("llm", catalog.Resolve("大規模言語モデル"));
    }

    [Fact]
    public void カタログに無いタグは落とさずそのまま返す()
    {
        // 落とすと新しいトピックが永久に入ってこなくなる
        var catalog = Catalog();

        Assert.Equal("わからない語", catalog.Resolve("わからない語"));
        Assert.Equal(["わからない語"], catalog.Normalize(["わからない語"]));
    }

    [Fact]
    public void 同義語をまとめたうえで重複を落とす()
    {
        var catalog = Catalog();

        var result = catalog.Normalize(["AI", "人工知能", "生成AI"]);

        Assert.Equal(["ai", "生成ai"], result);
    }

    [Fact]
    public void 粒度の違うトピックは統合しない()
    {
        // ai ⊃ 生成ai ⊃ llm は同義ではない。まとめると上位の語だけが巨大化する
        var catalog = Catalog();

        Assert.Equal(["ai", "生成ai", "llm"], catalog.Normalize(["AI", "生成AI", "LLM"]));
        Assert.Equal("生成ai", catalog.ParentOf("llm"));
        Assert.Null(catalog.ParentOf("ai"));
    }

    [Fact]
    public void 画面には正式表記を返す()
    {
        var catalog = Catalog();

        Assert.Equal("生成AI", catalog.DisplayOf("生成ai"));
        // カタログに無いキーはそのまま
        Assert.Equal("わからない語", catalog.DisplayOf("わからない語"));
    }

    [Fact]
    public void ストップワードはカタログを通しても落ちる()
    {
        Assert.Equal(["ai"], Catalog().Normalize(["あとで読む", "人工知能", "テクノロジー"]));
    }
}
