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

    /// <summary>1回のリクエストで渡す語数。詰めすぎると応答が MaxTokens で切れて JSON が壊れる。</summary>
    public const int BatchSize = 60;

    public async Task<IReadOnlyList<TopicClassifierVerdict>> ClassifyAsync(
        IReadOnlyList<string> tags,
        IReadOnlyList<TopicCatalogEntry> existingTopics,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var verdicts = new List<TopicClassifierVerdict>();
        var totalBatches = (tags.Count + BatchSize - 1) / BatchSize;

        for (var offset = 0; offset < tags.Count; offset += BatchSize)
        {
            var batch = tags.Skip(offset).Take(BatchSize).ToList();
            progress?.Invoke($"バッチ {offset / BatchSize + 1}/{totalBatches}({batch.Count} 語)を分類中");
            if (offset > 0)
            {
                // API への連続リクエストを避けるための間隔
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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
                            Content = TopicClassificationPrompt.ForTags(batch, existingTopics),
                        },
                    ],
                },
                cancellationToken: cancellationToken);

            var text = string.Join(
                "",
                response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();

            // 応答の番号はバッチ内の 1 始まりなので、全体の番号へずらして返す
            var batchOffset = offset;
            using var doc = JsonDocument.Parse(ExtractJson(text));
            verdicts.AddRange(TopicClassificationPrompt.ReadVerdicts(doc.RootElement)
                .Select(verdict => verdict with { Index = verdict.Index + batchOffset }));
        }

        return verdicts;
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
