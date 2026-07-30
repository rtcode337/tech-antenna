namespace TechAntenna.Core.Topics;

/// <summary>1つのタグについて、記事・イベント・書籍がそれぞれ何件あるか。</summary>
public record Topic(string Tag, int ArticleCount, int EventCount, int BookCount)
{
    /// <summary>記事・イベント・書籍のうち、何種類がそろっているか(0〜3)。</summary>
    public int Coverage =>
        (ArticleCount > 0 ? 1 : 0) + (EventCount > 0 ? 1 : 0) + (BookCount > 0 ? 1 : 0);

    public int Total => ArticleCount + EventCount + BookCount;
}
