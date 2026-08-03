using TechAntenna.Core;

namespace TechAntenna.Tests.Core;

public class TagNormalizerTests
{
    [Fact]
    public void 小文字化とトリムを行う()
    {
        var result = TagNormalizer.Normalize([" C# ", "Blazor"]);

        Assert.Equal(["c#", "blazor"], result);
    }

    [Fact]
    public void 正規化後に重複するタグは1つにまとめる()
    {
        var result = TagNormalizer.Normalize(["dotnet", "DotNet", " dotnet "]);

        Assert.Equal(["dotnet"], result);
    }

    [Fact]
    public void 空白のみのタグは取り除く()
    {
        var result = TagNormalizer.Normalize(["", "  ", "blazor"]);

        Assert.Equal(["blazor"], result);
    }

    [Fact]
    public void 出現順を保つ()
    {
        var result = TagNormalizer.Normalize(["Zenn", "Qiita", "zenn"]);

        Assert.Equal(["zenn", "qiita"], result);
    }

    [Fact]
    public void 区切りの有無で別のタグに割れない()
    {
        // 実データで割れていた組。Qiita のタグを語彙に入れるとこの形が増える
        var result = TagNormalizer.Normalize(["Claude Code", "claudecode", "claude-code", "claude_code"]);

        Assert.Equal(["claudecode"], result);
    }

    [Fact]
    public void 全角英数と半角カナをそろえる()
    {
        var result = TagNormalizer.Normalize(["ＡＩ", "ai", "ｼﾞｪﾈﾚｰﾃｨﾌﾞai", "ジェネレーティブAI"]);

        Assert.Equal(["ai", "ジェネレーティブai"], result);
    }

    [Fact]
    public void 語の一部になる記号は残す()
    {
        // 落とすと .net が net に、c# が c に、c++ が c になって別の語と衝突する
        var result = TagNormalizer.Normalize([".NET", "C#", "C++", "ASP.NET Core"]);

        Assert.Equal([".net", "c#", "c++", "asp.netcore"], result);
    }

    [Fact]
    public void トピックでないタグは落とす()
    {
        var result = TagNormalizer.Normalize(["テクノロジー", "あとで読む", "初心者", "生成AI"]);

        Assert.Equal(["生成ai"], result);
    }

    [Fact]
    public void ストップワードだけなら空になる()
    {
        var result = TagNormalizer.Normalize(["あとで読む", "メモ"]);

        Assert.Empty(result);
    }
}
