using TechAntenna.Core;

namespace TechAntenna.Tests.Core;

public class TagNormalizerTests
{
    [Fact]
    public void 小文字化とトリムを行う()
    {
        var result = TagNormalizer.Normalize([" C# ", "ASP.NET Core"]);

        Assert.Equal(["c#", "asp.net core"], result);
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
}
