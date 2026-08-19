using System.Text.Json;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>
/// Hugging Face Daily Papers からいま話題の論文を拾う。
///
/// トピックの選択に依存しないのがこの収集元の役目 —— arXiv と J-STAGE は検索なので
/// 収集対象を選んでいないと 1 件も集まらないが、こちらは「外で何が話題か」を
/// そのまま持ってくる(人が投稿して upvote する場なので、選別が済んでいる)。
///
/// 日本語の論文は入らない。中身は arXiv への投稿で、事実上すべて英語
/// (実測: 50 件中、タイトルに日本語を含むものは 0 件)。日本語の論文は J-STAGE 側の担当。
///
/// 取り込むのはタイトル・URL・投稿日・upvote 数・要旨。
/// 要旨を取り込んでよいのは arXiv のメタデータが CC0 だから —— arXiv の API Terms of Use が
/// 「descriptive metadata について CC0 1.0 の下で自由に利用できる」と明記していて、
/// ここの `summary` はその要旨そのもの(書籍の `description` は出版社の著作物なので別扱い)。
/// 要旨があると論文も要約の対象にできる(以前は材料が無いので対象外だった)。
/// リンク先は arXiv の abs ページにする(読みに行く先はそちらで、重複判定も arXiv の URL でそろう)。
/// </summary>
public class HuggingFacePapersSource(
    IHttpClientFactory httpClientFactory,
    TopicCatalog? catalog = null) : IArticleSource
{
    public const string HttpClientName = "huggingface-papers";

    const string Endpoint = "https://huggingface.co/api/daily_papers";

    public string Name => "Hugging Face Daily Papers";

    public async Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var topics = catalog ?? TopicCatalog.Empty;

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(Endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var articles = new List<Article>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("paper", out var paper)
                || paper.ValueKind != JsonValueKind.Object
                || GetString(paper, "title") is not { Length: > 0 } title
                || GetString(paper, "id") is not { Length: > 0 } id)
            {
                continue;
            }

            // arXiv の ID(`2608.01492`)から abs ページの URL を作る。http/https だけ通す
            if (!WebUrl.TryCreate($"https://arxiv.org/abs/{id}", out var url))
            {
                continue;
            }

            articles.Add(new Article
            {
                Title = title.Trim(),
                Url = url,
                SourceName = Name,
                Kind = ArticleKind.TrendingPaper,
                PublishedAt = ParseDate(paper, item),
                CollectedAt = DateTimeOffset.UtcNow,
                // 話題の度合い。一覧はこれで並べる(新着順だと「話題」の軸が出ない)
                UpvoteCount = GetInt(paper, "upvotes"),
                // 要旨(arXiv のメタデータ = CC0)。要約の材料になる
                ContentSnippet = GetString(paper, "summary")?.Trim(),
                // タイトルから見つかるトピックをタグにする(この収集元はタグを持たない)。
                // 英語のタイトルなので、カタログの英語別名に当たる
                RawTags = topics.FindIn(title),
                Tags = topics.Normalize(topics.FindIn(title)),
            });
        }

        return articles;
    }

    /// <summary>
    /// 投稿日。論文の公開日(`paper.publishedAt`)を優先し、無ければ Daily に載った日を使う
    /// (一覧は新着順に並べるので、日付が空だと最後に沈む)。
    /// </summary>
    static DateTimeOffset? ParseDate(JsonElement paper, JsonElement item) =>
        GetDate(paper, "publishedAt") ?? GetDate(item, "publishedAt");

    static DateTimeOffset? GetDate(JsonElement element, string name) =>
        GetString(element, name) is { Length: > 0 } value
            && DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;

    static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
