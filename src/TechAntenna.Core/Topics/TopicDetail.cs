using TechAntenna.Core.Models;

namespace TechAntenna.Core.Topics;

/// <summary>1つのタグに紐づく記事・イベント・書籍。</summary>
public record TopicDetail(
    string Tag,
    IReadOnlyList<Article> Articles,
    IReadOnlyList<TechEvent> Events,
    IReadOnlyList<Book> Books);
