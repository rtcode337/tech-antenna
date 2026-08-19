namespace TechAntenna.Core.Models;

/// <summary>勉強会・カンファレンス等のイベント。</summary>
public class TechEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    /// <summary>イベントページの URL。重複判定のキーとして使う。</summary>
    public required Uri Url { get; init; }

    /// <summary>収集元の名前(例: connpass、Doorkeeper)。</summary>
    public required string SourceName { get; init; }

    public required DateTimeOffset StartsAt { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>開催場所。オンライン開催のみ・未定の場合は null。</summary>
    public string? Venue { get; init; }

    public bool IsOnline { get; init; }

    /// <summary>
    /// 主催者(主催グループ)の名前。「公式のイベントか」の判定材料
    /// (<see cref="OfficialOrganizers"/>)で、connpass はグループ名、Doorkeeper は
    /// グループ名、TECH PLAY は取れないので null。
    ///
    /// 判定結果ではなく名前を保存する —— 名簿は画面から直せるので、
    /// 直したときに過去のイベントにも効いてほしい(<c>RawTags</c> と同じ考え方)。
    /// 収集しなおしで後から埋まることがあるので init ではなく set。
    /// </summary>
    public string? Organizer { get; set; }

    /// <summary>
    /// 参加者数(connpass の <c>accepted</c>、Doorkeeper の <c>participants</c>)。
    /// null は「取得していない」、0 は「まだ誰も参加していない」で別物
    /// (書籍の <c>ReviewCount</c>・記事の <c>BookmarkCount</c> と同じ規則)——
    /// TECH PLAY の RSS には数が無いので、そちらは常に null になる。
    /// 開催が近づくほど増えるので、既存のイベントでも収集のたびに取り直す。
    /// </summary>
    public int? ParticipantCount { get; set; }

    /// <summary>
    /// <b>なぜトピックの選択に関係なくこのイベントを載せているか</b>の理由。
    /// 選択したトピックの検索で見つかったものは null。
    ///
    /// 値は人が読める短い語(購読しているグループの表示名、「参加者 100 人以上」)で、
    /// <b>判定の印ではなく理由の控え</b> —— 画面はこれをそのまま出して、
    /// 興味トピックに当たらないイベントが一覧にいる訳を説明する。
    ///
    /// キーワード検索だけだと固有名詞のカンファレンス(RubyKaigi・DroidKaigi)が
    /// 構造的に落ちるので、<b>グループ購読</b>(<see cref="FollowedGroups"/>)と
    /// <b>参加者数での面掃き</b>という2つの別経路を用意してある。この2つで入ったものは
    /// 検索語に当たらないのが当たり前なので、収集でも表示でもトピックの絞りから外す。
    /// 収集しなおしで後から埋まることがあるので init ではなく set。
    /// </summary>
    public string? PickedBy { get; set; }

    /// <summary>
    /// このイベントに言及している記事の本数(<see cref="EventMentions"/>)。
    /// <b>参加者数の取れない収集元でも測れる注目度</b>で、記事を集めているこのアプリだから持てる指標。
    /// null は「測っていない」、0 は「まだ誰も書いていない」で別物
    /// (参加者数・はてブ数と同じ規則)—— 照合語を作れないイベントは常に null のまま。
    /// 記事は後から増えるので、収集のたびに数え直す。
    /// </summary>
    public int? MentionCount { get; set; }

    public required DateTimeOffset CollectedAt { get; init; }

    /// <summary>
    /// 正規化済みのタグ(<see cref="TagNormalizer"/> を通した値)。突き合わせに使う。
    /// init ではなく set なのは、正規化の規則を変えたときに <c>RawTags</c> から作り直すため。
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>
    /// 収集元から受け取ったままのタグ。正規化の規則を変えたら、ここから引き直す。
    /// 正規化後の値しか持たないと、別名カタログを直しても過去のデータに反映できない
    /// (`claude code` を `claudecode` に寄せた後で分けたくなっても、元の表記が残っていない)。
    /// </summary>
    public IReadOnlyList<string> RawTags { get; init; } = [];
}
