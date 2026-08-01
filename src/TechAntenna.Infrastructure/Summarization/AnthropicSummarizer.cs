using Anthropic;
using Anthropic.Models.Messages;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// Anthropic API(Messages API)で記事の日本語要約を生成する。従量課金だが、
/// 呼び出しごとの固定費が小さいので記事ごとに1リクエストを投げる。
/// </summary>
public class AnthropicSummarizer(string apiKey, string model) : ISummarizer
{
    readonly AnthropicClient _client = new() { ApiKey = apiKey };

    public string Name => "Anthropic API";

    public async Task<IReadOnlyList<SummaryResult>> SummarizeAsync(
        IReadOnlyList<Article> articles,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SummaryResult>();

        foreach (var article in articles)
        {
            // 材料の無い記事は API を呼ばずに空の要約として確定させる
            if (!SummaryPrompt.CanSummarize(article))
            {
                results.Add(new SummaryResult(article.Id, null));
                continue;
            }

            results.Add(new SummaryResult(
                article.Id, await SummarizeOneAsync(article, cancellationToken)));

            // API への連続リクエストを避けるための間隔
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return results;
    }

    async Task<string?> SummarizeOneAsync(Article article, CancellationToken cancellationToken)
    {
        var response = await _client.Messages.Create(
            new MessageCreateParams
            {
                Model = model,
                MaxTokens = 1024,
                System = SummaryPrompt.System + "要約本文だけを出力する。",
                Messages = [new() { Role = Role.User, Content = SummaryPrompt.ForArticle(article) }],
            },
            cancellationToken: cancellationToken);

        // セーフティ機構による拒否など、テキストが返らないケースは要約なしとして扱う
        var text = string.Join(
            "",
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
        return text.Length > 0 ? text : null;
    }
}
