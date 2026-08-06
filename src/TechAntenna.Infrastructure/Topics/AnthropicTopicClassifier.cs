using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Topics;

/// <summary>
/// Anthropic API(Messages API)で未知タグを分類する。従量課金だが呼び出しの固定費が
/// 小さい。とはいえ既存ツリーを毎回渡すので、こちらも**1回にまとめて**呼ぶ。
/// </summary>
public class AnthropicTopicClassifier(string apiKey, string model) : ITopicClassifier
{
    readonly AnthropicClient _client = new() { ApiKey = apiKey };

    public string Name => "Anthropic API";

    public async Task<IReadOnlyList<TopicClassifierVerdict>> ClassifyAsync(
        IReadOnlyList<string> tags,
        IReadOnlyList<TopicCatalogEntry> existingTopics,
        CancellationToken cancellationToken = default)
    {
        if (tags.Count == 0)
        {
            return [];
        }

        var response = await _client.Messages.Create(
            new MessageCreateParams
            {
                Model = model,
                MaxTokens = 8192,
                System = TopicClassificationPrompt.System
                    + "応答は指定の JSON だけを出力する。前置きも説明も書かない。"
                    + "形式: " + TopicClassificationPrompt.Schema,
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = TopicClassificationPrompt.ForTags(tags, existingTopics),
                    },
                ],
            },
            cancellationToken: cancellationToken);

        var text = string.Join(
            "",
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();

        using var doc = JsonDocument.Parse(ExtractJson(text));
        return TopicClassificationPrompt.ReadVerdicts(doc.RootElement);
    }

    /// <summary>
    /// 応答テキストから JSON 部分を取り出す。指示どおりなら全体が JSON だが、
    /// コードフェンスや前置きが付くことがあるので、最初の '{' から最後の '}' までを読む。
    /// </summary>
    static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start
            ? text[start..(end + 1)]
            : throw new FormatException("応答に JSON が見つからない。");
    }
}
