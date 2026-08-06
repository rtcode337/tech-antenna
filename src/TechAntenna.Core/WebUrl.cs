namespace TechAntenna.Core;

/// <summary>
/// 外部データから受け取った URL の検証。**http/https の絶対 URL だけを通す**。
///
/// `Uri.TryCreate(…, UriKind.Absolute)` は `javascript:alert(1)` のような URL も
/// 絶対 URI として通してしまい、取り込んだ値は画面の `&lt;a href&gt;` や
/// `&lt;img src&gt;` にそのまま出るため、収集元が悪意ある値を返すとクリック時に
/// 発火する格納型 XSS になる。リンクとして意味を成すのは http/https だけなので、
/// 取り込み時にそれ以外を落とす。
/// </summary>
public static class WebUrl
{
    /// <summary>http/https の絶対 URL なら true。それ以外(相対・別スキーム・null)は false。</summary>
    public static bool TryCreate(string? value, out Uri url)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            url = parsed;
            return true;
        }

        url = null!;
        return false;
    }

    /// <summary>http/https の絶対 URL でなければ FormatException を投げる。</summary>
    public static Uri Require(string? value) =>
        TryCreate(value, out var url)
            ? url
            : throw new FormatException($"http/https の絶対 URL でない: '{value}'");
}
