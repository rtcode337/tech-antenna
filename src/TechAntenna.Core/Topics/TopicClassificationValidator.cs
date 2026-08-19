using TechAntenna.Core.Abstractions;

namespace TechAntenna.Core.Topics;

/// <summary>検証を通った分類の結果。タグの仕分けと、新しく作るトピックに分かれる。</summary>
public record TopicClassification(
    IReadOnlyList<TagDecision> Decisions,
    IReadOnlyList<Topic> NewTopics);

/// <summary>
/// LLM が返した分類(<see cref="TopicClassifierVerdict"/>)を検証して、
/// タグの仕分け(<see cref="TagDecision"/>)と新トピックに直す。
///
/// LLM の応答は信じすぎないのがここの仕事:
/// 存在しないトピックへの寄せ・自分自身への寄せ・実在しない親は捨てる。
/// 捨てた語は <see cref="TagStatus.Unresolved"/> として期限付きで保留する ——
/// 捨てて何も残さないと毎回同じ語を聞き直して LLM の枠を無駄にする。
/// </summary>
public static class TopicClassificationValidator
{
    public static TopicClassification Validate(
        IReadOnlyList<string> tags,
        IReadOnlyList<TopicClassifierVerdict> verdicts,
        TopicCatalog catalog,
        DateTimeOffset decidedAt,
        int unknownRetryDays)
    {
        // 同じ番号が複数来たら最初のものを採る
        var byIndex = new Dictionary<int, TopicClassifierVerdict>();
        foreach (var verdict in verdicts)
        {
            byIndex.TryAdd(verdict.Index, verdict);
        }

        // 新トピックどうしの親子(A の親が同じバッチの B)を許すため、
        // 先に「新トピックとして通りそうな表記のキー」を集めておく
        var newTopicKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (index, verdict) in byIndex)
        {
            if (index >= 1 && index <= tags.Count
                && verdict.Kind == "new"
                && verdict.Display is { Length: > 0 } display)
            {
                newTopicKeys.Add(TagNormalizer.ToKey(display));
            }
        }

        var decisions = new List<TagDecision>();
        var newTopics = new Dictionary<string, Topic>(StringComparer.Ordinal);

        for (var i = 1; i <= tags.Count; i++)
        {
            var tag = tags[i - 1];

            // 応答に無い番号も期限付きの保留(毎回聞き直さない)
            if (!byIndex.TryGetValue(i, out var verdict))
            {
                decisions.Add(Unresolved(tag, decidedAt, unknownRetryDays));
                continue;
            }

            decisions.Add(Decide(
                tag, verdict, catalog, newTopicKeys, newTopics, decidedAt, unknownRetryDays));
        }

        return new TopicClassification(decisions, newTopics.Values.ToList());
    }

    static TagDecision Unresolved(string tag, DateTimeOffset decidedAt, int retryDays) =>
        new(tag, TagStatus.Unresolved, RetryAfter: decidedAt.AddDays(retryDays));

    static TagDecision Decide(
        string tag,
        TopicClassifierVerdict verdict,
        TopicCatalog catalog,
        HashSet<string> newTopicKeys,
        Dictionary<string, Topic> newTopics,
        DateTimeOffset decidedAt,
        int retryDays)
    {
        switch (verdict.Kind)
        {
            case "alias" when verdict.Target is { Length: > 0 } target:
            {
                var targetKey = catalog.Resolve(target);

                // 寄せ先が実在し、自分自身でないときだけ通す
                return !catalog.Contains(targetKey) || targetKey == tag
                    ? Unresolved(tag, decidedAt, retryDays)
                    : new TagDecision(tag, TagStatus.Alias, targetKey);
            }

            case "new" when verdict.Display is { Length: > 0 } display:
            {
                var key = TagNormalizer.ToKey(display);
                if (key.Length == 0)
                {
                    return Unresolved(tag, decidedAt, retryDays);
                }

                // 既にあるトピックの表記を「新トピック」と言ってきたら、寄せ先として扱い直す
                if (catalog.IsTopic(key))
                {
                    return key == tag
                        ? new TagDecision(tag, TagStatus.Promoted, key)
                        : new TagDecision(tag, TagStatus.Alias, key);
                }

                // 親は「実在するトピック」か「同じバッチで通る新トピック」だけ。自分自身は不可
                string? parentKey = null;
                if (verdict.Target is { Length: > 0 } parent)
                {
                    var candidate = catalog.Resolve(parent);
                    if (candidate != key
                        && (catalog.IsTopic(candidate) || newTopicKeys.Contains(candidate)))
                    {
                        parentKey = candidate;
                    }
                }

                newTopics[key] = new Topic
                {
                    Key = key,
                    Display = display.Trim(),
                    Parent = parentKey,
                    English = verdict.English,
                    Description = verdict.Description,
                    DecidedBy = DecidedBy.Llm,
                };

                // タグと正式表記のキーが違うなら、タグの側は別名として寄せる
                return key == tag
                    ? new TagDecision(tag, TagStatus.Promoted, key)
                    : new TagDecision(tag, TagStatus.Alias, key);
            }

            case "skip":
                return new TagDecision(tag, TagStatus.NotTopic);

            default:
                // unknown(語を知らない・新しすぎる)・未知の kind・必須値の欠けは、
                // 期限付きの保留にする。保存しないと毎回同じ語を聞き直して枠を無駄にし、
                // 無期限に確定させると、まさにツリーに入れたい新語を取り逃す
                return Unresolved(tag, decidedAt, retryDays);
        }
    }
}
