namespace TechAntenna.Core.Models;

/// <summary>
/// サマリーの守備範囲。1回の生成で2本作る —— 「界隈で何が起きたか」と
/// 「自分の興味に何があったか」は読む目的が違い、1本にまとめると興味の話が
/// 全体の話に埋もれる。通知も別々に届いたほうが、読む・読み飛ばすを選べる。
/// </summary>
public enum DigestScope
{
    /// <summary>技術界隈全体。興味トピックの選択に依らず、話題度の高いものだけで作る。</summary>
    Overall,

    /// <summary>興味トピック。選んだトピックに当たる記事・イベントだけで作る
    /// (トピックが1つも選ばれていなければ作らない)。</summary>
    Interests,
}

public static class DigestScopes
{
    /// <summary>
    /// 画面・通知・結果の文言に出す名前。1か所から出す ——
    /// 表記が割れると、同じサマリーが画面と通知で別物に見えるため。
    /// </summary>
    public static string Label(this DigestScope scope) => scope switch
    {
        DigestScope.Interests => "興味トピック",
        _ => "技術界隈全体",
    };
}

/// <summary>
/// 「今日のサマリー」1本分。収集した情報をもとに LLM がまとめた、押さえておくべき
/// 情報のダイジェスト。守備範囲(<see cref="DigestScope"/>)ごとに最新の1件を
/// ホームに出す。
/// </summary>
public class Digest
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>守備範囲(全体 / 興味トピック)。ホームと通知はこれで出し分ける。</summary>
    public required DigestScope Scope { get; init; }

    /// <summary>生成した日時(UTC)。最新の1件を選ぶキー。</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>全体の導入(1〜2文)。何が動いている日かをまず一言で言う。</summary>
    public required string Lead { get; init; }

    /// <summary>押さえておく項目。多すぎると読まれないので生成時に数個へ絞らせる。</summary>
    public required IReadOnlyList<DigestItem> Items { get; init; }

    /// <summary>生成した方式(Claude Code / Anthropic API / Gemini …)。画面の但し書きに出す。</summary>
    public required string GeneratorName { get; init; }

    /// <summary>
    /// 生成に使った相手の識別子(`chiezo:gemini` / `default` など)。表示名とは別に持つ ——
    /// 表示名はモデル名まで含んで変わりうるので、突き合わせのキーには使えない。
    ///
    /// 入れるのは生成を頼んだ側(<c>DigestRunner</c>)。誰に頼んだか・何本目かは
    /// 呼び出し側の都合で、書き手(<c>IDigestComposer</c>)の知る話ではない。
    /// </summary>
    public string GeneratorKey { get; set; } = "";

    /// <summary>
    /// 同じ回で作った束の識別子。複数の AI で同時に作るので、比較する相手は
    /// 「同じ回のもの」でなければならない —— 生成時刻で寄せると、失敗した AI の
    /// 前日ぶんが今日のものと並んでしまう。
    /// </summary>
    public Guid RunId { get; set; }

    /// <summary>メインの AI で作ったか。ホームの既定の表示と、通知に使う1本を選ぶ。</summary>
    public bool IsPrimary { get; set; }
}

/// <summary>ダイジェストの1項目。</summary>
/// <param name="Title">見出し(1行)。</param>
/// <param name="Body">本文(2〜3文)。</param>
/// <param name="Url">出典。材料に含めた URL をそのまま写させたもので、無ければ null。</param>
public record DigestItem(string Title, string Body, string? Url);
