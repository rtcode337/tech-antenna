using System.Text;
using System.Text.Json;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// ダイジェスト(今日のサマリー)の指示文・入力・スキーマ・応答の読み取り。
/// 要約(<see cref="SummaryPrompt"/>)と同じく、Claude Code 版と Anthropic API 版で
/// 同じ文面を使い、方式を切り替えても口調が変わらないようにする。
/// </summary>
public static class DigestPrompt
{
    /// <summary>どちらの守備範囲でも変わらない書き方の指示(体裁・出典の扱い)。</summary>
    const string Common =
        "lead は全体の導入1〜2文。items は3〜6項目で、各項目は title(見出し1行)と " +
        "body(2〜3文。なぜ押さえるべきかまで書く)。" +
        "似た話題の記事は1項目にまとめてよい。" +
        "url には**その項目の根拠にした材料の URL をそのまま写す**(複数あれば代表1つ。" +
        "材料に無い URL を作らない)。材料に書かれていないことを補わない。";

    /// <summary>
    /// 守備範囲ごとの指示文。**2本を別々の目で書かせる** —— 同じ指示で材料だけ変えると、
    /// どちらも「話題の総ざらい」になって読み分けられない。全体は界隈の動き、
    /// 興味トピックは読者の関心に引きつけて書かせる。
    /// </summary>
    public static string SystemFor(DigestScope scope) => scope switch
    {
        DigestScope.Interests =>
            "あなたは技術情報のダイジェストを書く編集者。" +
            "渡された材料(読者の興味トピックに当たる記事・これからのイベント)から、" +
            "**その読者が押さえておくべきもの**を選び、日本語で短いダイジェストを書く。" +
            "**読者の興味トピックとの関係が分かるように書く**(どのトピックの話なのか、" +
            "何をすればよいのか)。界隈全体の一般論には広げない。" + Common,
        _ =>
            "あなたは技術情報のダイジェストを書く編集者。渡された材料(直近の話題)から、" +
            "**技術界隈全体としていま押さえておくべきもの**を選び、日本語で短いダイジェストを書く。" +
            "特定の読者の好みには寄せず、界隈で何が動いているかを書く。" + Common,
    };

    /// <summary>
    /// 構造化出力のスキーマ。url は空文字で「無し」を表す(nullable にすると
    /// 方式ごとの null の扱いの差を踏むため、文字列に寄せて読み取りで捨てる)。
    /// </summary>
    // 波括弧だらけなので、生文字列の補間ではなく素直に連結する(ClaudeCodeBatch と同じ流儀)
    public const string Schema =
        "{\"type\":\"object\",\"properties\":{"
        + "\"lead\":{\"type\":\"string\"},"
        + "\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
        + "\"title\":{\"type\":\"string\"},\"body\":{\"type\":\"string\"},"
        + "\"url\":{\"type\":\"string\"}},"
        + "\"required\":[\"title\",\"body\"]}}},"
        + "\"required\":[\"lead\",\"items\"]}";

    /// <summary>画面が破綻しないよう、応答の項目数はここで打ち切る(指示だけに任せない)。</summary>
    public const int MaxItems = 8;

    /// <summary>材料をまとめた入力を組み立てる。見出しは守備範囲で変える。</summary>
    public static string ForMaterials(DigestMaterials materials)
    {
        var builder = new StringBuilder();

        if (materials.SelectedTopics.Count > 0)
        {
            builder.AppendLine("## 読者の興味トピック");
            builder.AppendLine(string.Join("、", materials.SelectedTopics));
            builder.AppendLine();
        }

        AppendArticles(
            builder,
            materials.Scope == DigestScope.Interests
                ? "## 興味トピックに当たる直近の記事"
                : "## 直近の話題(話題度の高い順)",
            materials.Articles);

        if (materials.UpcomingEvents.Count > 0)
        {
            builder.AppendLine("## これからのイベント(興味トピック)");
            foreach (var techEvent in materials.UpcomingEvents)
            {
                builder.AppendLine(
                    // 日時は JST で渡す(LLM が本文に書き写すので、UTC のままだと読者と 9 時間ずれる)
                    $"- {JapanTime.Format(techEvent.StartsAt)} {techEvent.Title}"
                    + $"({(techEvent.IsOnline ? "オンライン" : techEvent.Venue ?? "会場未定")})"
                    + $" URL: {techEvent.Url}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    static void AppendArticles(StringBuilder builder, string heading, IReadOnlyList<Article> articles)
    {
        if (articles.Count == 0)
        {
            return;
        }

        builder.AppendLine(heading);
        foreach (var article in articles)
        {
            // 話題度は「はてブ」「upvote」のあるほうを出す(両方 0 なら出さない)
            var counts = new List<string>();
            if (article.BookmarkCount is > 0)
            {
                counts.Add($"はてブ {article.BookmarkCount}");
            }

            if (article.UpvoteCount is > 0)
            {
                counts.Add($"upvote {article.UpvoteCount}");
            }

            builder.AppendLine(
                $"- [{Label(article.Kind)}] {article.TitleJa ?? article.Title}"
                + (counts.Count > 0 ? $"({string.Join("・", counts)})" : "")
                + $" URL: {article.Url}");

            // 要約か本文抜粋があれば1行だけ添える(全文は渡さない —— トークンを浪費する)
            var snippet = article.Summary is { Length: > 0 } summary
                ? summary
                : article.ContentSnippet;
            if (!string.IsNullOrWhiteSpace(snippet))
            {
                builder.AppendLine($"  {Excerpt(snippet)}");
            }
        }

        builder.AppendLine();
    }

    static string Label(ArticleKind kind) => kind switch
    {
        ArticleKind.News => "ニュース",
        ArticleKind.Paper or ArticleKind.TrendingPaper => "論文",
        _ => "記事",
    };

    /// <summary>スニペットの改行を畳んで頭だけ渡す。</summary>
    static string Excerpt(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 200 ? flat : flat[..200] + "…";
    }

    /// <summary>
    /// 応答(構造化出力)の JSON からダイジェストを組む。**URL は材料に含めたものしか
    /// 通さない** —— LLM が作った URL を画面のリンクにしないため(WebUrl と同じ発想の検証)。
    /// </summary>
    public static Digest Read(
        JsonElement output,
        DigestMaterials materials,
        string generatorName,
        DateTimeOffset generatedAt)
    {
        var knownUrls = materials.Articles.Select(a => a.Url.ToString())
            .Concat(materials.UpcomingEvents.Select(e => e.Url.ToString()))
            .ToHashSet(StringComparer.Ordinal);

        var items = new List<DigestItem>();
        if (output.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in array.EnumerateArray())
            {
                // 上限は**有効な項目**で数える(壊れた項目に枠を食わせない)
                if (items.Count >= MaxItems)
                {
                    break;
                }

                var title = element.TryGetProperty("title", out var t) ? t.GetString() : null;
                var body = element.TryGetProperty("body", out var b) ? b.GetString() : null;
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                var url = element.TryGetProperty("url", out var u) ? u.GetString() : null;
                items.Add(new DigestItem(
                    title.Trim(),
                    body.Trim(),
                    url is { Length: > 0 } && knownUrls.Contains(url) ? url : null));
            }
        }

        var lead = output.TryGetProperty("lead", out var l) ? l.GetString() : null;
        if (string.IsNullOrWhiteSpace(lead) && items.Count == 0)
        {
            throw new FormatException("ダイジェストの応答に lead も items も無い。");
        }

        return new Digest
        {
            Scope = materials.Scope,
            GeneratedAt = generatedAt,
            Lead = lead?.Trim() ?? "",
            Items = items,
            GeneratorName = generatorName,
        };
    }
}
