using TechAntenna.Core.Abstractions;

namespace TechAntenna.Core.Topics;

/// <summary>
/// LLM が返した分類(<see cref="TopicClassifierVerdict"/>)を検証して、
/// カタログに反映してよい <see cref="TopicClassification"/> に直す。
///
/// **LLM の応答は信じすぎない**のがここの仕事:
/// 存在しないトピックへの寄せ・自分自身への寄せ・実在しない親は捨てる。
/// 捨てた語は保存もしない(次の収集でもう一度 LLM に聞く)。
/// </summary>
public static class TopicClassificationValidator
{
    public static IReadOnlyList<TopicClassification> Validate(
        IReadOnlyList<string> tags,
        IReadOnlyList<TopicClassifierVerdict> verdicts,
        TopicCatalog catalog,
        DateTimeOffset classifiedAt)
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

        var results = new List<TopicClassification>();
        for (var i = 1; i <= tags.Count; i++)
        {
            // 応答に無い番号は保存しない(Skip として確定させず、次回もう一度聞く)
            if (!byIndex.TryGetValue(i, out var verdict))
            {
                continue;
            }

            var tag = tags[i - 1];
            var classification = ToClassification(tag, verdict, catalog, newTopicKeys, classifiedAt);
            if (classification is not null)
            {
                results.Add(classification);
            }
        }

        return results;
    }

    static TopicClassification? ToClassification(
        string tag,
        TopicClassifierVerdict verdict,
        TopicCatalog catalog,
        HashSet<string> newTopicKeys,
        DateTimeOffset classifiedAt)
    {
        switch (verdict.Kind)
        {
            case "alias" when verdict.Target is { Length: > 0 } target:
            {
                var targetKey = catalog.Resolve(target);
                // 寄せ先が実在し、自分自身でないときだけ通す
                if (!catalog.Contains(targetKey) || targetKey == tag)
                {
                    return null;
                }

                return new TopicClassification
                {
                    Tag = tag,
                    Kind = TopicClassificationKind.Alias,
                    TargetKey = targetKey,
                    ClassifiedAt = classifiedAt,
                };
            }

            case "new" when verdict.Display is { Length: > 0 } display:
            {
                var displayKey = TagNormalizer.ToKey(display);

                // 「新トピック」と言いながら実在する表記なら、同義語として扱い直す
                // (表記のキーがタグ自身なら、既に載っているので何も足すことが無い)
                if (catalog.Contains(displayKey))
                {
                    var resolved = catalog.Resolve(display);
                    return resolved == tag
                        ? null
                        : new TopicClassification
                        {
                            Tag = tag,
                            Kind = TopicClassificationKind.Alias,
                            TargetKey = resolved,
                            ClassifiedAt = classifiedAt,
                        };
                }

                // 親は「実在するトピック」か「同じバッチで通る新トピック」だけ。自分自身は不可
                string? parentKey = null;
                if (verdict.Target is { Length: > 0 } parent)
                {
                    var candidate = catalog.Resolve(parent);
                    if (candidate != displayKey
                        && (catalog.Contains(candidate) || newTopicKeys.Contains(candidate)))
                    {
                        parentKey = candidate;
                    }
                }

                return new TopicClassification
                {
                    Tag = tag,
                    Kind = TopicClassificationKind.NewTopic,
                    Display = display.Trim(),
                    ParentKey = parentKey,
                    ClassifiedAt = classifiedAt,
                };
            }

            case "skip":
                return new TopicClassification
                {
                    Tag = tag,
                    Kind = TopicClassificationKind.Skip,
                    ClassifiedAt = classifiedAt,
                };

            default:
                // unknown(語を知らない・新しすぎる)・未知の kind・必須値の欠けは保存しない。
                // **skip と違って次回もう一度聞く** —— 新語は時間が経てば分類できるようになるので、
                // 「分からない」を確定させると、まさにツリーに入れたい新しいトピックを取り逃す
                return null;
        }
    }
}
