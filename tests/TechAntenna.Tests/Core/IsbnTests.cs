using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class IsbnTests
{
    [Fact]
    public void 書籍のASINをISBN13に直す()
    {
        // 書籍の ASIN は ISBN-10 そのもの(記事に貼られた Amazon リンクから書誌を引くため)
        Assert.Equal("9784873112756", Isbn.FromAsin("4873112753"));
        Assert.Equal("9784797382228", Isbn.FromAsin("4797382228"));
    }

    [Fact]
    public void 書籍でないASINは弾く()
    {
        // Kindle 専売や電子機器の ASIN は ISBN-10 として成り立たない。
        // チェックディジットまで検算しないと、存在しない ISBN を書誌照会に投げてしまう
        Assert.Null(Isbn.FromAsin("B00KR96M6K"));
        Assert.Null(Isbn.FromAsin("B0176GNY26"));
        Assert.Null(Isbn.FromAsin("4873112754")); // 1桁違い
        Assert.Null(Isbn.FromAsin("487311275"));  // 桁不足
        Assert.Null(Isbn.FromAsin(null));
    }

    [Fact]
    public void 末尾がXのISBN10も扱える()
    {
        // 達人プログラマー(原書)の ISBN-10
        Assert.True(Isbn.IsValidIsbn10("020161622X"));
        Assert.Equal("9780201616224", Isbn.FromAsin("020161622X"));
    }
}
