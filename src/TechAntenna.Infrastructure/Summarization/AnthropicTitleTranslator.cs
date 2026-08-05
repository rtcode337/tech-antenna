using Anthropic;
using Anthropic.Models.Messages;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// Anthropic API(Messages API)でタイトルを訳す。呼び出しごとの固定費が小さいので
/// 1件ずつ投げる(要約と同じ方針)。
/// </summary>
public class AnthropicTitleTranslator(string apiKey, string model) : ITitleTranslator
{
    readonly AnthropicClient _client = new() { ApiKey = apiKey };

    public string Name => "Anthropic API";

    public async Task<IReadOnlyList<TitleTranslation>> TranslateAsync(
        IReadOnlyList<Article> articles,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TitleTranslation>();

        foreach (var article in articles)
        {
            // 日本語のタイトルは API を呼ばずに、訳さないものとして確定させる
            if (!TitleTranslationPrompt.NeedsTranslation(article))
            {
                results.Add(new TitleTranslation(article.Id, null));
                continue;
            }

            results.Add(new TitleTranslation(
                article.Id, await TranslateOneAsync(article, cancellationToken)));

            // API への連続リクエストを避けるための間隔
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return results;
    }

    async Task<string?> TranslateOneAsync(Article article, CancellationToken cancellationToken)
    {
        var response = await _client.Messages.Create(
            new MessageCreateParams
            {
                Model = model,
                // 訳題1行なので短くてよい
                MaxTokens = 256,
                System = TitleTranslationPrompt.System,
                Messages = [new() { Role = Role.User, Content = article.Title }],
            },
            cancellationToken: cancellationToken);

        // セーフティ機構による拒否など、テキストが返らないケースは訳なしとして扱う
        var text = string.Join(
            "",
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
        return text.Length > 0 ? text : null;
    }
}
