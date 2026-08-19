using TechAntenna.Core;

namespace TechAntenna.Web.Services;

/// <summary>
/// 購読しているグループの名簿(設定 → 購読)。
///
/// 値は API キー・定期実行の設定・公式の名簿と同じく DB(<c>Secrets</c>)に持つので、
/// 再起動なしで効き、コンテナを作り直しても残る。環境変数では設定できない ——
/// 入口が 2 つあると「どちらの名簿が効いているのか」を画面が説明し続けることになる
/// (<see cref="OrganizerSettings"/> と同じ扱い)。
///
/// <b>初期値は持たない。</b> シリーズ ID もコミュニティ名も実在の値なので、
/// リポジトリに憶測で書けない —— 未設定なら「この経路では何も集めない」が正しい状態。
/// </summary>
public static class FollowSettings
{
    /// <summary>名簿の設定キー(1行1件で保存する)。</summary>
    public const string GroupsName = "Events:FollowedGroups";

    /// <summary>いま効いている名簿。<b>収集元と画面の両方がここから取る。</b></summary>
    public static FollowedGroups Resolve(ApiCredentials credentials) =>
        FollowedGroups.Parse(credentials.Get(GroupsName));

    /// <summary>保存されている生のテキスト(画面の入力欄に戻すため)。</summary>
    public static string? Raw(ApiCredentials credentials) => credentials.Get(GroupsName);
}
