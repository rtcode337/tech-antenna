using System.Text.Json;
using TechAntenna.Infrastructure.Chiezo;

namespace TechAntenna.Web.Services;

/// <summary>
/// どの AI に書かせるかの選択。**Chiezo(LAN 内の知識サーバー)に登録してある相手**から選ぶ。
///
/// メインは LLM を使う全部のジョブ(要約・翻訳・タグの仕分け・今日のサマリー)で使う。
/// **サブは今日のサマリーだけ** —— 比べて読みたいのは文章で、要約や翻訳は枚数が多く
/// 読み比べられないため(呼び出しも保存も相手の数だけ増える)。
///
/// 値は API キーと同じく DB(<c>Secrets</c>)に持ち、実行のたびに読むので再起動なしで効く。
/// 形は JSON 1 本 —— 相手・モデル・考える量の 3 つ組が何組も入るので、
/// 独自の区切り文字で組み立てると、モデル名に区切りが混ざったときに壊れる。
/// </summary>
public static class AiSettings
{
    /// <summary>設定キー(値は下の <see cref="AiConfig"/> の JSON)。</summary>
    public const string ConfigName = "Ai:Config";

    /// <summary>サブに選べる数の上限。ホームのタブが増えすぎると読み比べにならない。</summary>
    public const int MaxSubs = 4;

    public static AiConfig Load(ApiCredentials credentials)
    {
        var raw = credentials.Get(ConfigName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return AiConfig.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<AiConfig>(raw) ?? AiConfig.Empty;
        }
        catch (JsonException)
        {
            // 形が変わった・壊れた値は「未設定」として扱う(画面から選び直せる)
            return AiConfig.Empty;
        }
    }

    public static Task SaveAsync(
        ApiCredentials credentials, AiConfig config, CancellationToken cancellationToken = default) =>
        config.Main is null
            ? credentials.RemoveAsync(ConfigName, cancellationToken)
            : credentials.SetAsync(ConfigName, JsonSerializer.Serialize(config), cancellationToken);
}

/// <summary>選んだ相手 1 つ。</summary>
/// <param name="Backend">Chiezo 側の識別子(`gemini` など)。</param>
/// <param name="Label">画面に出す名前。**選んだ時点の表記を持つ** —— 表示のたびに
/// Chiezo へ問い合わせると、繋がらない日にホームの但し書きが消える。</param>
/// <param name="Model">モデル(空なら相手の既定)。</param>
/// <param name="Effort">考える量(空なら相手の既定)。</param>
public record AiChoice(string Backend, string Label, string? Model, string? Effort)
{
    public ChiezoAiSelection ToSelection() => new(Backend, Label, Model, Effort);

    public string Key => $"chiezo:{Backend}";
}

/// <summary>メイン 1 つとサブ複数。</summary>
public record AiConfig(AiChoice? Main, IReadOnlyList<AiChoice> Subs)
{
    public static readonly AiConfig Empty = new(null, []);

    /// <summary>メインを先頭にした全部(重複する相手は落とす)。</summary>
    public IReadOnlyList<AiChoice> All() => Main is null
        ? []
        : new[] { Main }
            .Concat(Subs.Where(sub => sub.Backend != Main.Backend))
            .DistinctBy(choice => choice.Backend, StringComparer.Ordinal)
            .ToList();
}
