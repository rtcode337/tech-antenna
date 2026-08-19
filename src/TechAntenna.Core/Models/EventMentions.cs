using System.Text.RegularExpressions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Models;

/// <summary>
/// イベントが<b>記事でどれだけ言及されているか</b>を数える。
///
/// イベントの規模は参加者数で測っていたが、これは<b>参加者数を出す収集元でしか測れない</b> ——
/// TECH PLAY の RSS には数が無く、自社サイトで完結する大型カンファレンスは
/// そもそも参加者数を公開しない。結果、注目度順にすると「connpass に載っている中規模の
/// 勉強会」が上に来て、RubyKaigi のような年1回の大物が沈む。
///
/// このアプリは<b>記事をすでに集めている</b>ので、そこを使う ——
/// <b>そのイベントについて何本の記事が書かれたか</b>は、参加者数と違って収集元を問わず測れるし、
/// 「注目度」の定義としても素直(参加者 200 人でも誰も書かない社内向けセミナーは沈む)。
/// </summary>
public static class EventMentions
{
    /// <summary>
    /// 照合語の最短の長さ。<b>短い語は必ず誤爆する</b> ——「AI」を照合語にすると
    /// AI の記事すべてがそのイベントへの言及になってしまう。
    /// </summary>
    const int MinKeyLength = 5;

    /// <summary>
    /// イベントの名前に付く飾り。<b>先に外す</b> ——「【東京開催】RubyKaigi 2026」を
    /// そのまま照合語にすると、記事側の「RubyKaigi 2026 に参加した」に当たらない。
    /// </summary>
    static readonly Regex Decorations = new(
        @"[【\[（(〈《「『＜<][^】\]）)〉》」』＞>]*[】\]）)〉》」』＞>]", RegexOptions.Compiled);

    /// <summary>回数・年・ハッシュタグ。イベント名の本体ではないので落とす。</summary>
    static readonly Regex Ordinals = new(
        @"(第\s*\d+\s*[回弾部]|#\S+|\b(19|20)\d{2}\b年?|vol\.?\s*\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>名前と副題の区切り。最初の区切りより前だけを名前とみなす。</summary>
    static readonly char[] Separators = ['|', '｜', '/', '／', '~', '〜', '－', '―', '–', '—', ':', '：'];

    /// <summary>
    /// これだけになった照合語は捨てる。<b>どのイベントにも付く一般名</b>なので、
    /// 記事側の「もくもく会に行った」を全部そのイベントへの言及として数えてしまう。
    /// </summary>
    static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "勉強会", "もくもく会", "ハンズオン", "ミートアップ", "meetup", "交流会", "懇親会",
        "読書会", "輪読会", "セミナー", "ウェビナー", "webinar", "説明会", "相談会", "登壇",
        "オンライン勉強会", "オンラインセミナー", "カンファレンス", "conference", "lt会",
    };

    /// <summary>
    /// 記事と突き合わせる語。作れなければ null(= <b>そのイベントは言及数を測らない</b>)。
    ///
    /// 出どころは<b>タイトルだけ</b>にしてある。ハッシュタグのほうが照合語には向くが、
    /// <c>RawTags</c> の中では検索キーワードと区別が付かない(connpass は
    /// <c>[検索語, ハッシュタグ]</c> の順で入れるが、ハッシュタグの無いイベントもある)ため、
    /// 「タグの2番目」のような当て推量に頼ることになる。<b>規則は1本にする</b>。
    /// </summary>
    /// <param name="catalog">
    /// 技術の語彙。<b>照合語が技術名そのものになったら捨てる</b>ために使う ——
    /// 「Kubernetes」という名前のイベントの言及数を数えると、Kubernetes の記事が全部当たる。
    /// </param>
    public static string? KeyFor(TechEvent techEvent, TopicCatalog? catalog = null)
    {
        var name = Decorations.Replace(techEvent.Title, " ");

        // 副題を落とす(「RubyKaigi 2026 | 参加者募集」→「RubyKaigi 2026」)
        var cut = name.IndexOfAny(Separators);
        if (cut > 0)
        {
            name = name[..cut];
        }

        name = Ordinals.Replace(name, " ");
        // 飾りを外したあとに残る記号と連続した空白を片付ける
        name = Regex.Replace(name, @"[\s　]+", " ").Trim(' ', '　', '-', '_', '.', '、', '。', '!', '！');

        if (name.Length < MinKeyLength || Generic.Contains(name))
        {
            return null;
        }

        // 技術名そのもの(「生成AI」「Kubernetes」)は名前ではないので測らない
        return catalog?.Contains(TagNormalizer.ToKey(name)) == true ? null : name;
    }

    /// <summary>
    /// その照合語に当たる記事の本数。<b>照合は <see cref="KeywordMatcher"/></b> ——
    /// 収集元の検索と同じ規則で、「AI」が「Rails」に当たらないところまで揃える。
    /// 見るのはタイトル(と訳題)だけで<b>要約や本文は見ない</b> ——
    /// 本文まで見ると「関連イベントの告知」を1本の言及として数えてしまう。
    /// </summary>
    public static int Count(string key, IEnumerable<Article> articles) =>
        articles.Count(article =>
            KeywordMatcher.Contains(article.Title, key)
            || KeywordMatcher.Contains(article.TitleJa, key));
}
