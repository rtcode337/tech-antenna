using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// 語彙の初期投入。**`topic-seed.json` は「人が確定させた語彙」ではなく初期データ**なので、
/// <b>DB が空のときに一度だけ流し込む</b>(`DecidedBy = Seed`)。以後の権威は DB にあり、
/// JSON との衝突ルールは持たない —— 手直しは画面から状態を書き換える。
///
/// **初期データを残しているのは 2 つの理由から。** ひとつは、まったく語彙が無いと LLM が
/// 寄せ先も親も選べず、同義の親が二重にできること(統合パスで後から寄せられるが、
/// 最初から避けられるほうがよい)。もうひとつは、**新着記事だけから育てるとカテゴリが偏る**こと
/// —— その時期に流れてきた話題に語彙が寄ってしまうので、広めの土台を先に与える。
/// </summary>
public class TopicSeeder(
    ITopicStore topicStore,
    ITagStore tagStore,
    ILogger<TopicSeeder> logger,
    TimeProvider clock)
{
    /// <summary>DB が空なら投入する。投入した語彙の数を返す(空でなければ 0)。</summary>
    public async Task<int> SeedAsync(
        IReadOnlyList<TopicCatalogEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0 || (await topicStore.GetAllAsync(cancellationToken)).Count > 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();

        var topics = entries
            .Select(entry => new Topic
            {
                Key = entry.Key,
                Display = entry.Display,
                Parent = entry.Parent is { Length: > 0 } parent ? TagNormalizer.ToKey(parent) : null,
                English = entry.English
                    ?? entry.Aliases.FirstOrDefault(alias => alias.All(char.IsAscii)),
                Description = entry.Description,
                DecidedBy = DecidedBy.Seed,
            })
            .ToList();

        await topicStore.UpsertAsync(topics, now, cancellationToken);

        // 正式表記のタグは Promoted、別名のタグは Alias。**観測してから仕分ける** ——
        // タグの行が無いと仕分けの書き込み先が無い(件数はこの時点では 0 のまま)
        var observations = new List<TagObservation>();
        var decisions = new List<TagDecision>();
        foreach (var entry in entries)
        {
            observations.Add(new TagObservation(entry.Key));
            decisions.Add(new TagDecision(entry.Key, TagStatus.Promoted, entry.Key, DecidedBy.Seed));

            foreach (var alias in entry.Aliases.Select(TagNormalizer.ToKey)
                .Where(alias => alias.Length > 0 && alias != entry.Key)
                .Distinct(StringComparer.Ordinal))
            {
                observations.Add(new TagObservation(alias));
                decisions.Add(new TagDecision(alias, TagStatus.Alias, entry.Key, DecidedBy.Seed));
            }
        }

        await tagStore.ObserveAsync(observations, now, cancellationToken: cancellationToken);
        await tagStore.DecideAsync(decisions, now, cancellationToken);

        logger.LogInformation(
            "語彙の初期値を投入: トピック {Topics} 件・タグ {Tags} 件", topics.Count, observations.Count);

        return topics.Count;
    }
}
