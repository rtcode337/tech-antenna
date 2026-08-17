namespace TechAntenna.Core.Models;

/// <summary>
/// 集めたイベントを<b>主催者と収集元でまとめた</b>もの。購読の候補を出すために使う。
/// </summary>
/// <param name="Organizer">主催者名(<see cref="TechEvent.Organizer"/>)。</param>
/// <param name="SourceName">収集元の名前(<see cref="TechEvent.SourceName"/>)。</param>
/// <param name="SampleUrl">
/// その組のイベント 1 件の URL。<b>グループの識別子はここから起こす</b> ——
/// connpass も Doorkeeper もサブドメインがグループなので、保存済みの URL があれば
/// 外部へ問い合わせずに名簿の行を作れる(主催者名から ID は引けない)。
/// </param>
/// <param name="Count">その組で集まっているイベントの件数。</param>
public record OrganizerGroup(string Organizer, string SourceName, Uri SampleUrl, int Count);
