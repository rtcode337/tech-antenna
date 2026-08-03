using System.Text;
using TechAntenna.Core;

namespace TechAntenna.Web.Services;

/// <summary>
/// ログイン機能がない間、ブラウザごとの関心トピックを Cookie に保存する。
/// 関心トピックは表示の好みであり、第三者提供や追跡には使わない。
/// </summary>
public class InterestTopicCookie(IHttpContextAccessor httpContextAccessor)
{
    const string CookieName = "tech-antenna-interest-topics";

    public IReadOnlyList<string> Get()
    {
        var value = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            var encoded = Convert.FromBase64String(value);
            return TagNormalizer.Normalize(Encoding.UTF8.GetString(encoded)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (FormatException)
        {
            // 古い形式や壊れた Cookie では、設定なしとして安全に扱う
            return [];
        }
    }

    public void Save(IEnumerable<string> topics)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            return;
        }

        var normalized = TagNormalizer.Normalize(topics);
        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', normalized)));
        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps
        });
    }
}
