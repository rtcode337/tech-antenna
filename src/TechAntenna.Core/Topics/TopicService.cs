using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Core.Topics;

/// <summary>
/// 1つのトピックに紐づく記事・イベント・書籍を集めるサービス(トピックの詳細ページ用)。
/// 一覧の側は持たない —— 何を一覧に出すかは語彙(<see cref="ITopicStore"/>)が決めるので、
/// かつてここにあった「3種がそろったタグを上位に出す」一覧は使われなくなった。
/// </summary>
public class TopicService(
    IArticleStore articleStore,
    IEventStore eventStore,
    IBookStore bookStore)
{
    /// <summary>1つのタグに紐づく記事・イベント・書籍をまとめて取得する。</summary>
    public async Task<TopicDetail> GetTopicAsync(
        string tag,
        int perType,
        CancellationToken cancellationToken = default)
    {
        // 保存時と同じ正規化を通してから引く
        var normalized = TagNormalizer.Normalize([tag]).FirstOrDefault() ?? tag;

        return new TopicDetail(
            normalized,
            await articleStore.GetByTagAsync(normalized, perType, cancellationToken),
            await eventStore.GetByTagAsync(normalized, perType, cancellationToken),
            // 読んだ本は後ろへ回す(トピックの詳細でも一覧と同じ規則)。
            // ReadLast は安定な並べ替えなので、ストアの収集日時順は未読・既読それぞれの中で残る
            (await bookStore.GetByTagAsync(normalized, perType, cancellationToken))
                .ReadLast()
                .ToList());
    }
}
