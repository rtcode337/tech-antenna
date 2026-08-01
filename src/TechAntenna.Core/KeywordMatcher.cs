using System.Text;

namespace TechAntenna.Core;

/// <summary>
/// 検索語がテキストに実際に含まれるかを判定する。
///
/// 収集元の検索がこちらの意図より広く当たることがあるため(Doorkeeper の <c>q</c> は
/// 説明文まで検索し、<c>#</c> や <c>.</c> といった記号を落とすので「C#」が実質「C」になる)、
/// 取り込む前に手元で確かめ直すために使う。
///
/// 単純な部分一致だと「AI」が「Rails」「email」に当たってしまうので、
/// <b>検索語の端が英数字のときだけ、その側が英数字と地続きでないことを求める</b>。
/// 日本語は語が英数字で挟まれないため、この一つの規則で両方を扱える。
/// </summary>
public static class KeywordMatcher
{
    /// <summary>
    /// <paramref name="text"/> に <paramref name="keyword"/> が含まれるか。
    /// 全角と半角、大文字と小文字は区別しない。
    /// </summary>
    /// <example>
    /// 「生成AI最新ニュース」は「AI」に一致するが、「Rails もくもく会」は一致しない。
    /// 「ASP.NET Core」は「.NET」に一致し、「C#入門」は「C#」に一致する。
    /// </example>
    public static bool Contains(string? text, string keyword)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
        {
            return false;
        }

        var haystack = Normalize(text);
        var needle = Normalize(keyword);
        if (needle.Length == 0)
        {
            return false;
        }

        for (var start = 0; start + needle.Length <= haystack.Length;)
        {
            var found = haystack.IndexOf(needle, start, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            var end = found + needle.Length;
            var leftOk = !IsAsciiAlphanumeric(needle[0])
                || found == 0
                || !IsAsciiAlphanumeric(haystack[found - 1]);
            var rightOk = !IsAsciiAlphanumeric(needle[^1])
                || end == haystack.Length
                || !IsAsciiAlphanumeric(haystack[end]);

            if (leftOk && rightOk)
            {
                return true;
            }

            // 同じ語が後ろにも出るかもしれないので、1文字ずらして探し直す
            start = found + 1;
        }

        return false;
    }

    /// <summary>全角英数字を半角にそろえ(NFKC)、小文字に統一する。</summary>
    static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    static bool IsAsciiAlphanumeric(char c) =>
        char.IsAsciiLetterOrDigit(c);
}
