using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// 語彙とタグの仕分けをファイルに書き出す。
///
/// **用途は環境間の持ち運び。** LLM の仕分けは呼ぶたびに枠を使うので、本番で仕分けた結果を
/// 開発サーバーへ持って行けるようにする(逆向きにも使える)。バックアップにもなる。
///
/// **出すのは仕分けだけ**(件数・話題度は出さない。理由は <see cref="TopicExportFile"/>)。
/// </summary>
public class TopicExporter(ITopicStore topicStore, ITagStore tagStore, TimeProvider clock)
{
    public async Task<TopicExportFile> BuildAsync(CancellationToken cancellationToken = default)
    {
        var topics = await topicStore.GetAllAsync(cancellationToken);
        var tags = await tagStore.GetAllAsync(cancellationToken);

        return new TopicExportFile
        {
            Comment =
            [
                "tech-antenna のトピック(語彙)とタグの仕分けの持ち出しファイル。",
                "「設定 → トピック」の「ファイルで持ち出す / 取り込む」から書き出し、別の環境で取り込む。",
                "topics = 語彙。key は正規化済み、display が画面と検索語に使う正式表記、parent は1つ上の粒度。",
                "tags   = 見かけた語の仕分け。status が Alias なら topicKey が寄せ先、"
                    + "NotTopic はトピックでないと判定した語、Unresolved は LLM が判断できなかった語。",
                "**件数・話題度は入っていない** —— 取り込んだ環境が集めたデータの話なので、整備で集め直す。",
                "**未仕分け(Pending)のタグも入っていない** —— まだ何も決まっていないため。",
                "selected(収集対象の選択)は取り込み時に既定では使わない(画面で明示したときだけ反映する)。",
            ],
            ExportedAt = clock.GetUtcNow(),
            // 並びはキー順に固定する。書き出すたびに順番が変わると差分が読めない
            Topics = topics
                .OrderBy(topic => topic.Key, StringComparer.Ordinal)
                .Select(topic => new TopicExportEntry(
                    topic.Key,
                    topic.Display is { Length: > 0 } display ? display : topic.Key,
                    topic.Parent,
                    topic.English,
                    topic.Description,
                    topic.DecidedBy,
                    topic.IsSelected))
                .ToList(),
            // 仕分けの済んだタグだけ。Pending は「まだ聞いていない」という情報しか持たない
            Tags = tags
                .Where(tag => tag.Status != TagStatus.Pending)
                .OrderBy(tag => tag.Key, StringComparer.Ordinal)
                .Select(tag => new TagExportEntry(
                    tag.Key,
                    tag.Status,
                    tag.TopicKey,
                    tag.DecidedBy,
                    tag.DecidedAt,
                    tag.RetryAfter))
                .ToList(),
        };
    }
}
