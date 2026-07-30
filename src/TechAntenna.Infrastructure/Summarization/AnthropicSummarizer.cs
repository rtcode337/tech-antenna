using Anthropic;
using Anthropic.Models.Messages;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>Anthropic API(Messages API)で記事の日本語要約を生成する。</summary>
public class AnthropicSummarizer(string apiKey, string model) : ISummarizer
{
    const string SystemPrompt =
        "あなたは技術記事を要約するアシスタント。渡された記事の内容を、" +
        "技術者が一覧画面で読む前提で、日本語2〜3文に要約する。" +
        "記事に書かれていないことを補わない。要約本文だけを出力する。";

    readonly AnthropicClient _client = new() { ApiKey = apiKey };

    public async Task<string?> SummarizeAsync(Article article, CancellationToken cancellationToken = default)
    {
        // タイトルしか無い記事は要約する材料が足りない
        if (string.IsNullOrWhiteSpace(article.ContentSnippet))
        {
            return null;
        }

        var prompt = $"""
            タイトル: {article.Title}
            収集元: {article.SourceName}
            本文抜粋:
            {article.ContentSnippet}
            """;

        var response = await _client.Messages.Create(
            new MessageCreateParams
            {
                Model = model,
                MaxTokens = 1024,
                System = SystemPrompt,
                Messages = [new() { Role = Role.User, Content = prompt }],
            },
            cancellationToken: cancellationToken);

        // セーフティ機構による拒否など、テキストが返らないケースは要約なしとして扱う
        var text = string.Join(
            "",
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
        return text.Length > 0 ? text : null;
    }
}
