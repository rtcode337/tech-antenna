using TechAntenna.Core;

namespace TechAntenna.Web.Services;

/// <summary>
/// 「公式のイベント」と見なす主催者の名簿(設定 → 主催者)。
///
/// 値は API キー・定期実行の設定と同じく DB(<c>Secrets</c>)に持つので、
/// **再起動なしで効き、コンテナを作り直しても残る**。**環境変数では設定できない** ——
/// 入口が 2 つあると「どちらの名簿が効いているのか」を画面が説明し続けることになる
/// (<see cref="ScheduleSettings"/> と同じ扱い)。
///
/// 未設定なら <see cref="OfficialOrganizers.Defaults"/>(リポジトリに持つ初期値)。
/// </summary>
public static class OrganizerSettings
{
    /// <summary>名簿の設定キー(1行1名で保存する)。</summary>
    public const string OfficialNamesName = "Events:OfficialOrganizers";

    /// <summary>
    /// いま効いている名簿。**画面と並べ替えの両方がここから取る** ——
    /// 判定を1か所から出さないと、バッジの付いたイベントと注目度の順位が食い違う。
    /// </summary>
    public static OfficialOrganizers Resolve(ApiCredentials credentials) =>
        OfficialOrganizers.Parse(credentials.Get(OfficialNamesName));

    /// <summary>画面で書き換えた名簿か(初期値のままなら false)。</summary>
    public static bool IsCustomized(ApiCredentials credentials) =>
        !string.IsNullOrWhiteSpace(credentials.Get(OfficialNamesName));
}
