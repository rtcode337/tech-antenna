using TechAntenna.Core.Abstractions;

namespace TechAntenna.Core.Topics;

/// <summary>
/// 記事・イベント・書籍をタグで横断して見るためのサービス。
/// 「このトピックの記事がある → 関連する勉強会がある → 深掘りする本がある」を
/// 1つの導線にまとめるのがこのアプリの目的なので、3種がそろったタグを上位に出す。
/// </summary>
public class TopicService(
    IArticleStore articleStore,
    IEventStore eventStore,
    IBookStore bookStore)
{
    /// <summary>
    /// タグの一覧を、そろっている種類数の多い順、次いで総件数の多い順で返す。
    /// </summary>
    public async Task<IReadOnlyList<Topic>> GetTopicsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var articleTags = await articleStore.GetTagCountsAsync(cancellationToken);
        var eventTags = await eventStore.GetTagCountsAsync(cancellationToken);
        var bookTags = await bookStore.GetTagCountsAsync(cancellationToken);

        var articles = articleTags.ToDictionary(t => t.Tag, t => t.Count);
        var events = eventTags.ToDictionary(t => t.Tag, t => t.Count);
        var books = bookTags.ToDictionary(t => t.Tag, t => t.Count);

        return articles.Keys.Concat(events.Keys).Concat(books.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(tag => new Topic(
                tag,
                articles.GetValueOrDefault(tag),
                events.GetValueOrDefault(tag),
                books.GetValueOrDefault(tag)))
            .OrderByDescending(t => t.Coverage)
            .ThenByDescending(t => t.Total)
            .ThenBy(t => t.Tag, StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }

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
            await bookStore.GetByTagAsync(normalized, perType, cancellationToken));
    }
}
