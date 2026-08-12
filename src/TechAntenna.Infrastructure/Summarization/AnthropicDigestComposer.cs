using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// Anthropic API(Messages API)でダイジェストを書く。1回の生成が1リクエスト。
/// </summary>
public class AnthropicDigestComposer(string apiKey, string model, TimeProvider clock) : IDigestComposer
{
    readonly AnthropicClient _client = new() { ApiKey = apiKey };

    public string Name => "Anthropic API";

    public async Task<Digest> ComposeAsync(
        DigestMaterials materials, CancellationToken cancellationToken = default)
    {
        var response = await _client.Messages.Create(
            new MessageCreateParams
            {
                Model = model,
                MaxTokens = 4096,
                System = DigestPrompt.SystemFor(materials.Scope)
                    + "応答は指定の JSON だけを出力する。前置きも説明も書かない。"
                    + "形式: " + DigestPrompt.Schema,
                Messages =
                [
                    new() { Role = Role.User, Content = DigestPrompt.ForMaterials(materials) },
                ],
            },
            cancellationToken: cancellationToken);

        var text = string.Join(
            "",
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();

        using var doc = JsonDocument.Parse(ExtractJson(text));
        return DigestPrompt.Read(doc.RootElement, materials, Name, clock.GetUtcNow());
    }

    /// <summary>
    /// 応答テキストから JSON 部分を取り出す。指示どおりなら全体が JSON だが、
    /// コードフェンスや前置きが付くことがあるので、最初の '{' から最後の '}' までを読む
    /// (<c>AnthropicTopicClassifier</c> と同じ流儀)。
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
