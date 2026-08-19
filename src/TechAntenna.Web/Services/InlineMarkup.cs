using System.Text;
using Microsoft.AspNetCore.Components;

namespace TechAntenna.Web.Services;

/// <summary>
/// 画面に出す説明文の中のごく限られた装飾(強調とコード)を HTML に直す。
///
/// 説明文(<see cref="IntegrationCatalog"/> の <c>Description</c> など)は、コードのコメントと
/// 同じ書き方で `強調` と `` `コード` `` を使って書いてある。そのまま出すと記号が
/// 画面に見えるだけ(実際に「CLI は別コンテナ…」とアスタリスクごと出ていた)。
///
/// Markdown の実装は持ち込まない。要るのはこの2つだけで、見出し・箇条書き・
/// リンクを解釈させると、説明文の書き方に幅が出て画面の作りが崩れる。
///
/// 先に HTML を退避してから記号を置き換える —— 説明文は今のところ自前の文字列だけだが、
/// 順序を逆にすると、後から外部の値を通したときにそのままタグとして出てしまう。
/// </summary>
public static class InlineMarkup
{
    public static MarkupString ToHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new MarkupString("");
        }

        var escaped = new StringBuilder();
        foreach (var c in text)
        {
            escaped.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => c.ToString(),
            });
        }

        // 対になっているものだけを直す(片方しか無い記号はそのまま文字として残す)
        return new MarkupString(
            Wrap(Wrap(escaped.ToString(), "**", "strong"), "`", "code"));
    }

    static string Wrap(string text, string marker, string tag)
    {
        var result = new StringBuilder();
        var rest = text.AsSpan();
        while (true)
        {
            var open = rest.IndexOf(marker);
            if (open < 0)
            {
                result.Append(rest);
                return result.ToString();
            }

            var afterOpen = rest[(open + marker.Length)..];
            var close = afterOpen.IndexOf(marker);
            if (close < 0)
            {
                // 閉じが無い —— 記号のままにして残りをそのまま足す
                result.Append(rest);
                return result.ToString();
            }

            result.Append(rest[..open]);
            result.Append('<').Append(tag).Append('>');
            result.Append(afterOpen[..close]);
            result.Append("</").Append(tag).Append('>');
            rest = afterOpen[(close + marker.Length)..];
        }
    }
}
