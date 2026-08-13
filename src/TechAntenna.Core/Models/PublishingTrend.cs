namespace TechAntenna.Core.Models;

/// <summary>出版のテーマ 1 つぶん。</summary>
/// <param name="Tag">正規化済みのタグ(トピックのキー)。</param>
/// <param name="Count">その窓のあいだに出た冊数。</param>
/// <param name="Examples">代表のタイトル(新しい順)。数字だけだと何の話か分からないため。</param>
public record PublishingTheme(string Tag, int Count, IReadOnlyList<NewRelease> Examples);

/// <summary>
/// 最近出た本を**テーマ(タグ)ごとに数える**。「出版側がいまどのテーマに寄せているか」を、
/// はてブ数や upvote とは<b>別の指標</b>として出すためのもの ——
/// 記事の話題度が「速い反応」なら、こちらは<b>企画から刊行まで数か月かかる、遅くて重い動き</b>。
///
/// **数えるのはタイトルから拾ったタグ**なので、語彙(<c>TopicCatalog</c>)に無い言葉は出てこない。
/// それでよい —— ここで見たいのは「知っているテーマのうち、どれが厚くなっているか」で、
/// 未知語の発掘はタグの仕分けの仕事。
/// </summary>
public static class PublishingTrend
{
    /// <summary>1 テーマにつき出す代表タイトルの数。</summary>
    public const int ExamplesPerTheme = 2;

    /// <summary>
    /// テーマごとの冊数を多い順に返す。同数なら<b>新しい本を含むほう</b>を先に出す
    /// (同じ 3 冊なら、先月出たテーマのほうが「いま」に近い)。
    /// </summary>
    /// <param name="releases">窓で切った新刊(刊行日の新しい順)。</param>
    /// <param name="minCount">これ未満のテーマは出さない。1 冊だけのテーマは偶然が混じる。</param>
    /// <param name="limit">返すテーマの数。</param>
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
                pair.Value.Count,
                pair.Value
                    .OrderByDescending(release => release.PublishedOn)
                    .Take(ExamplesPerTheme)
                    .ToList()))
            .OrderByDescending(theme => theme.Count)
            .ThenByDescending(theme => theme.Examples.Max(release => release.PublishedOn))
            .ThenBy(theme => theme.Tag, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }
}
