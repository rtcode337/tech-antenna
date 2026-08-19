using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// 保存済みの生タグ(<c>RawTags</c>)から、記事・イベント・書籍のタグを作り直す。
///
/// 正規化の規則やストップワードを変えても、既に保存したデータは古い規則のままなので、
/// トピックの集計が新旧混在になる。それを直すためのジョブ。外部へは一切出ないので、
/// 何度走らせても安全(結果は毎回同じ)。
/// </summary>
public class TagRenormalizationRunner(
    TopicCatalog catalog,
    IArticleStore articleStore,
    IEventStore eventStore,
    IBookStore bookStore) : JobRunner
{
    public override string Name => "タグを再正規化";

    // 保存済みのデータだけを触るので、外部 API のキーの有無に関係なく常に実行できる
    public override bool IsConfigured => true;

    public Task<TagRenormalizationResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () => RenormalizeAsync(cancellationToken), TagRenormalizationResult.Nothing, cancellationToken);

    async Task<TagRenormalizationResult> RenormalizeAsync(CancellationToken cancellationToken) =>
        new(
            await articleStore.RenormalizeTagsAsync(catalog, cancellationToken),
            await eventStore.RenormalizeTagsAsync(catalog, cancellationToken),
            await bookStore.RenormalizeTagsAsync(catalog, cancellationToken));
}

/// <summary>再正規化でタグが変わった件数。</summary>
public record TagRenormalizationResult(int Articles, int Events, int Books)
{
    public static readonly TagRenormalizationResult Nothing = new(0, 0, 0);

    public int Total => Articles + Events + Books;
}
