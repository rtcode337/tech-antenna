namespace TechAntenna.Core.Models;

/// <summary>出版のテーマ 1 つぶん。</summary>
/// <param name="Tag">正規化済みのタグ(トピックのキー)。</param>
/// <param name="Books">
/// そのテーマの本(刊行の新しい順)。窓に入った分がすべて入っている ——
/// トレンドのまとめは先頭の数冊だけを代表として出し(<see cref="PublishingTrend.ExamplesPerTheme"/>)、
/// トレンドの書籍ページは全部を並べる。冊数を数えたのと同じ集合をそのまま持つので、
/// 見出しの「N 冊」と中身の行数が食い違わない。
/// </param>
public record PublishingTheme(string Tag, IReadOnlyList<NewRelease> Books)
{
    /// <summary>その窓のあいだに出た冊数。</summary>
    public int Count => Books.Count;
}

/// <summary>
/// 最近出た本をテーマ(タグ)ごとに数える。「出版側がいまどのテーマに寄せているか」を、
/// はてブ数や upvote とは<b>別の指標</b>として出すためのもの ——
/// 記事の話題度が「速い反応」なら、こちらは<b>企画から刊行まで数か月かかる、遅くて重い動き</b>。
///
/// 数えるのはタイトルから拾ったタグなので、語彙(<c>TopicCatalog</c>)に無い言葉は出てこない。
/// それでよい —— ここで見たいのは「知っているテーマのうち、どれが厚くなっているか」で、
/// 未知語の発掘はタグの仕分けの仕事。
/// </summary>
public static class PublishingTrend
{
    /// <summary>まとめの節で 1 テーマにつき出す代表タイトルの数(全部は書籍ページの担当)。</summary>
    public const int ExamplesPerTheme = 2;

    /// <summary>
    /// テーマごとの冊数を多い順に返す。同数なら<b>新しい本を含むほう</b>を先に出す
    /// (同じ 3 冊なら、先月出たテーマのほうが「いま」に近い)。
    /// </summary>
    /// <param name="releases">窓で切った新刊(刊行日の新しい順)。</param>
    /// <param name="minCount">これ未満のテーマは出さない。1 冊だけのテーマは偶然が混じる。</param>
    /// <param name="limit">返すテーマの数(全部欲しいときは <see cref="int.MaxValue"/>)。</param>
    public static IReadOnlyList<PublishingTheme> Themes(
        IEnumerable<NewRelease> releases, int minCount = 2, int limit = 12)
    {
        var byTag = new Dictionary<string, List<NewRelease>>(StringComparer.Ordinal);

        foreach (var release in releases)
        {
            // 同じ本に同じタグが 2 回付いていても 1 冊として数える
            foreach (var tag in release.Tags.Distinct(StringComparer.Ordinal))
            {
                if (!byTag.TryGetValue(tag, out var books))
                {
                    books = [];
                    byTag[tag] = books;
                }

                books.Add(release);
            }
        }

        return byTag
            .Where(pair => pair.Value.Count >= minCount)
            .Select(pair => new PublishingTheme(
                pair.Key,
                pair.Value
                    .OrderByDescending(release => release.PublishedOn)
                    .ToList()))
            .OrderByDescending(theme => theme.Count)
            .ThenByDescending(theme => theme.Books.Max(release => release.PublishedOn))
            .ThenBy(theme => theme.Tag, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }
}
