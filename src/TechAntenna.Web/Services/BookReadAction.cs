namespace TechAntenna.Web.Services;

/// <summary>
/// 「読んだ」の切り替えボタンが submit する値(`toggle:&lt;Id&gt;`)の組み立てと読み取り。
///
/// **書式を1か所に置く**ためだけのもの。ボタンを描くのは <c>BookItem</c>、受けるのは
/// 書籍を並べるページ(<c>/interests/books</c>・<c>/classics/books</c>)と離れていて、
/// どちらかだけ直すと押しても何も起きない形で静かに壊れる。
///
/// **値は状態で変えない**(立てる/下ろすで別の値にしない)—— <c>keep-focus.js</c> が
/// 押した値でボタンを探し直して位置を復元するので、POST の前後で値が変わると
/// 長い一覧の先頭へ戻ってしまう。裏返す判断は保存先が持つ
/// (<see cref="Core.Abstractions.IBookStore.ToggleReadAsync"/>)。
/// </summary>
public static class BookReadAction
{
    const string Prefix = "toggle:";

    /// <summary>ボタンの value。</summary>
    public static string Value(Guid id) => Prefix + id;

    /// <summary>
    /// 押されたボタンの value から本の Id を読む。**読めなければ false**
    /// (同じフォームに別の submit が増えても、ここで弾かれるだけで済む)。
    /// </summary>
    public static bool TryReadId(string? action, out Guid id)
    {
        id = Guid.Empty;

        return action is { Length: > 0 }
            && action.StartsWith(Prefix, StringComparison.Ordinal)
            && Guid.TryParse(action[Prefix.Length..], out id);
    }
}
