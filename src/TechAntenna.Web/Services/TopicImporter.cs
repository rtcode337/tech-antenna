using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Topics;

namespace TechAntenna.Web.Services;

/// <summary>取り込みの結果。画面に「何が起きたか」を出すために、落とした分も数える。</summary>
/// <param name="Topics">取り込んだ語彙の数。</param>
/// <param name="TopicsAdded">うち、この環境に無くて新しく増えた数。</param>
/// <param name="Tags">取り込んだタグの仕分けの数。</param>
/// <param name="TagsAdded">うち、この環境で見かけていなかった語の数。</param>
/// <param name="KeptHuman">この環境で人が直した仕分けを守って、取り込まなかった数。</param>
/// <param name="DroppedParents">親が見つからず、最上位として取り込んだ語の数。</param>
/// <param name="DroppedAliases">寄せ先が見つからず捨てた別名の数。</param>
/// <param name="Selected">収集対象として反映した数(選択を取り込まないときは 0)。</param>
public record TopicImportResult(
    int Topics,
    int TopicsAdded,
    int Tags,
    int TagsAdded,
    int KeptHuman,
    int DroppedParents,
    int DroppedAliases,
    int Selected);

/// <summary>
/// 持ち出しファイルから語彙とタグの仕分けを取り込む。
///
/// 足し込みで、消さない。この環境にしか無い語彙・仕分けはそのまま残す ——
/// 取り込みは「別の環境で仕分けた結果を合わせる」操作で、置き換えではない。
///
/// 観測(件数・話題度)は触らない。ファイルにも入っていないし、あるのは取り込む側の
/// 実データの話なので、上書きすると一覧の件数が嘘になる(次の整備が集め直す)。
/// </summary>
public class TopicImporter(
    ITopicStore topicStore,
    ITagStore tagStore,
    TopicCatalogRefresher catalogRefresher,
    ILogger<TopicImporter> logger,
    TimeProvider clock)
{
    /// <param name="importSelection">
    /// 収集対象の選択(`selected`)も反映するか。既定で反映しない ——
    /// 収集キーワードが黙って変わると、イベント・書籍の問い合わせ先が勝手に増減する。
    /// </param>
    public async Task<TopicImportResult> ImportAsync(
        TopicExportFile file,
        bool importSelection = false,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        var stored = (await topicStore.GetAllAsync(cancellationToken))
            .ToDictionary(topic => topic.Key, StringComparer.Ordinal);
        var existingTags = (await tagStore.GetAllAsync(cancellationToken))
            .ToDictionary(tag => tag.Key, StringComparer.Ordinal);

        // 既存の行も全部渡す。UpsertAsync は渡されなかった行の件数と話題度を 0 にするので、
        // 取り込む語だけを渡すと、この環境が集めた件数が消える
        var merged = new List<Topic>(stored.Values);
        // 取り込みで触った語。親の検算はこの分だけに掛ける —— この環境にもとからあった
        // 行まで直すと、取り込みが関係ない場所を書き換えることになる
        var touched = new HashSet<string>(StringComparer.Ordinal);
        var applied = 0;
        var added = 0;
        var keptHuman = 0;

        // 1周目: 行を作る・語彙を上書きする(親の検算は全部そろってからでないとできない)
        foreach (var entry in file.Topics)
        {
            var key = TagNormalizer.ToKey(entry.Key is { Length: > 0 } ? entry.Key : entry.Display);
            if (key.Length == 0)
            {
                continue;
            }

            if (!stored.TryGetValue(key, out var topic))
            {
                topic = new Topic { Key = key };
                stored[key] = topic;
                merged.Add(topic);
                added++;
            }
            else if (topic.DecidedBy == DecidedBy.Human && entry.DecidedBy != DecidedBy.Human)
            {
                // 人が直した語彙は守る。LLM より人の判断を優先するのは画面の手直しと同じ規則
                keptHuman++;
                continue;
            }

            applied++;
            touched.Add(key);
            topic.Display = entry.Display is { Length: > 0 } display ? display : key;
            topic.Parent = entry.Parent is { Length: > 0 } parent ? TagNormalizer.ToKey(parent) : null;
            topic.English = entry.English;
            topic.Description = entry.Description;
            // 出どころはファイルの値をそのまま入れる。書かれていなければ「誰も決めていない」のまま ——
            // 勝手に Llm や Human を補うと、次の取り込みで守る/上書きするの判断が嘘の根拠で決まる
            topic.DecidedBy = entry.DecidedBy;
        }

        // 2周目: 実在しない親と自分自身への親は落として最上位にする。ファイルを信じない ——
        // 手で編集されることもあるし、循環したままだとツリーを描く側が延々とたどる
        var droppedParents = 0;
        foreach (var topic in merged.Where(topic =>
            touched.Contains(topic.Key)
            && topic.Parent is { Length: > 0 } parent
            && (parent == topic.Key || !stored.ContainsKey(parent))))
        {
            topic.Parent = null;
            droppedParents++;
        }

        await topicStore.UpsertAsync(merged, now, cancellationToken);

        // タグ: 仕分けを書く前に行が要る(DecideAsync は無いタグを作らない)。
        // 観測するのは「この環境で見かけていない語」だけ —— 既存の語を観測し直すと
        // 件数 0 で上書きしてしまう
        var decisions = new List<(TagDecision Decision, DateTimeOffset DecidedAt)>();
        var newKeys = new List<TagObservation>();
        var droppedAliases = 0;

        foreach (var entry in file.Tags)
        {
            var key = TagNormalizer.ToKey(entry.Key);
            if (key.Length == 0 || entry.Status == TagStatus.Pending)
            {
                continue;
            }

            var topicKey = entry.TopicKey is { Length: > 0 } target ? TagNormalizer.ToKey(target) : null;

            // 寄せ先が無い別名は捨てる(どこにも紐づかない仕分けは害にしかならない)
            if (entry.Status == TagStatus.Alias
                && (topicKey is null || topicKey == key || !stored.ContainsKey(topicKey)))
            {
                droppedAliases++;
                continue;
            }

            if (existingTags.TryGetValue(key, out var tag))
            {
                if (tag.DecidedBy == DecidedBy.Human && entry.DecidedBy != DecidedBy.Human)
                {
                    keptHuman++;
                    continue;
                }
            }
            else
            {
                newKeys.Add(new TagObservation(key));
            }

            decisions.Add((
                new TagDecision(
                    key,
                    entry.Status,
                    entry.Status == TagStatus.Promoted ? key : topicKey,
                    entry.DecidedBy,
                    entry.RetryAfter),
                entry.DecidedAt ?? now));
        }

        if (newKeys.Count > 0)
        {
            await tagStore.ObserveAsync(newKeys, now, cancellationToken: cancellationToken);
        }

        // 判定日時は持ち出し元のものを保つので、時刻ごとにまとめて書く ——
        // 全部を「取り込んだ時刻」にすると、「同じ実行で付けた分類は時刻が揃う」を頼りに
        // 「前回聞いた語」を復元している画面が、取り込み全体を1回の分類として出してしまう
        foreach (var group in decisions.GroupBy(decision => decision.DecidedAt))
        {
            await tagStore.DecideAsync(
                group.Select(decision => decision.Decision).ToList(), group.Key, cancellationToken);
        }

        var selected = 0;
        if (importSelection)
        {
            // 選択は「渡した分で置き換える」のが store の約束。配下への広げ直しはしない ——
            // 持ち出し元で保存したときに広げた結果がそのまま入っている
            var keys = file.Topics
                .Where(entry => entry.Selected)
                .Select(entry => TagNormalizer.ToKey(entry.Key is { Length: > 0 } ? entry.Key : entry.Display))
                .Where(key => key.Length > 0 && stored.ContainsKey(key))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            await topicStore.UpdateSelectionAsync(keys, cancellationToken);
            selected = keys.Count;
        }

        await catalogRefresher.RefreshAsync(cancellationToken);

        var result = new TopicImportResult(
            applied,
            added,
            decisions.Count,
            newKeys.Count,
            keptHuman,
            droppedParents,
            droppedAliases,
            selected);

        logger.LogInformation(
            "トピックを取り込み: 語彙 {Topics} 件(新規 {Added})・仕分け {Tags} 件(新規 {TagsAdded})"
                + "・人の判断を守って見送り {KeptHuman} 件",
            result.Topics, result.TopicsAdded, result.Tags, result.TagsAdded, result.KeptHuman);

        return result;
    }
}
