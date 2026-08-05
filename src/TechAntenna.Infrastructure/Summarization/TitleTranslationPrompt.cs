using System.Text;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// タイトル翻訳の指示文と入力の組み立て。Anthropic API 版と Claude Code 版で同じ文面を使う。
/// </summary>
public static class TitleTranslationPrompt
{
    public const string System =
        "あなたは学術論文のタイトルを日本語に訳すアシスタント。" +
        "渡された英語のタイトルを、日本語の学術文献で使われる表現で簡潔に訳す。" +
        "**定訳のある専門用語は日本語にし、定訳が無い固有名詞・手法名・略語(Transformer、RAG 等)は" +
        "原語のまま残す**。説明を足さず、訳題だけを返す。";

    /// <summary>
    /// 複数件をまとめた入力。1 始まりの番号で参照し、応答もその番号で返させる
    /// (記事の Id をそのまま渡すと、LLM が長い GUID を写し間違える余地ができるため)。
    /// </summary>
    public static string ForArticles(IReadOnlyList<Article> articles)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < articles.Count; i++)
        {
            builder.AppendLine($"{i + 1}. {articles[i].Title}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// 訳す必要があるか。**日本語(漢字・かな)を含むタイトルは訳さない** ——
    /// J-STAGE の論文はもともと和題なので、投げるだけ枠の無駄になる。
    /// </summary>
    public static bool NeedsTranslation(Article article) =>
        !string.IsNullOrWhiteSpace(article.Title) && !ContainsJapanese(article.Title);

    static bool ContainsJapanese(string text) =>
        text.Any(c =>
            c is >= '぀' and <= 'ヿ'      // ひらがな・カタカナ
                or >= '一' and <= '鿿'    // 漢字
                or >= 'ｦ' and <= 'ﾝ');  // 半角カナ
}
