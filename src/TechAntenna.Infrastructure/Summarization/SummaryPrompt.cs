using System.Text;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// 要約の指示文と入力の組み立て。Anthropic API 版と Claude Code 版で同じ文面を使い、
/// 実装を切り替えても要約の口調が変わらないようにする。
/// </summary>
public static class SummaryPrompt
{
    public const string System =
        "あなたは技術記事を要約するアシスタント。渡された記事の内容を、" +
        "技術者が一覧画面で読む前提で、日本語2〜3文に要約する。" +
        "記事に書かれていないことを補わない。";

    /// <summary>1記事分の入力。</summary>
    public static string ForArticle(Article article) =>
        $"""
        タイトル: {article.Title}
        収集元: {article.SourceName}
        本文抜粋:
        {article.ContentSnippet}
        """;

    /// <summary>
    /// 複数記事をまとめた入力。記事は 1 始まりの番号で参照し、応答もその番号で返させる
    /// (記事の Id をそのまま渡すと、LLM が長い GUID を写し間違える余地ができるため)。
    /// </summary>
    public static string ForArticles(IReadOnlyList<Article> articles)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < articles.Count; i++)
        {
            builder.AppendLine($"### 記事 {i + 1}");
            builder.AppendLine(ForArticle(articles[i]));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>要約する材料があるか。タイトルしか無い記事は要約しようがない。</summary>
    public static bool CanSummarize(Article article) =>
        !string.IsNullOrWhiteSpace(article.ContentSnippet);
}
