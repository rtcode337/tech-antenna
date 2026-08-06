using Microsoft.Extensions.Logging.Abstractions;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Trends;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class TopicReorganizationRunnerTests
{
    class StubTrendSource(IReadOnlyList<TrendTopicCandidate> candidates) : ITrendTopicSource
    {
        public string Name => "スタブ";

        public Task<IReadOnlyList<TrendTopicCandidate>> FetchAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(candidates);
    }

    static TopicReorganizationRunner NewRunner(TopicCatalog catalog, ITrendTopicSource source, ITopicStore topicStore)
    {
        var articles = new InMemoryArticleStore();
        var events = new InMemoryEventStore();
        var books = new InMemoryBookStore();
        var classifications = new InMemoryTopicClassificationStore();

        return new TopicReorganizationRunner(
            catalog,
            [source],
            topicStore,
            articles,
            events,
            books,
            classifications,
            new TopicCandidateFinder(
                catalog, articles, events, books, classifications, TimeProvider.System),
            new TagRenormalizationRunner(catalog, articles, events, books),
            NullLogger<TopicReorganizationRunner>.Instance,
            TimeProvider.System);
    }

    [Fact]
    public async Task 話題度は親へ合算される()
    {
        // 親(プログラミング言語)は構造の語で自身の話題度が付かない。
        // 合算しないと一覧の取得数から押し出され、子が根として孤立して見える
        var catalog = new TopicCatalog(
        [
            new TopicCatalogEntry("プログラミング", [], null),
            new TopicCatalogEntry("プログラミング言語", [], "プログラミング"),
            new TopicCatalogEntry("Python", [], "プログラミング言語"),
            new TopicCatalogEntry("Rust", [], "プログラミング言語"),
        ]);
        var store = new InMemoryTopicStore();
        var runner = NewRunner(catalog, new StubTrendSource(
        [
            new TrendTopicCandidate("Python", 30, "スタブ"),
            new TrendTopicCandidate("Rust", 10, "スタブ"),
        ]), store);

        await runner.RunOnceAsync();

        var topics = (await store.GetTopicsAsync(10)).ToDictionary(t => t.Tag);
        // 単体はソース内シェア(合計に対する割合 × 100)なので Python=75, Rust=25
        Assert.Equal(75, topics["python"].TrendScore, precision: 5);
        Assert.Equal(25, topics["rust"].TrendScore, precision: 5);
        // 合算は自身 + 配下の合計。親自身の単体は 0 のまま
        Assert.Equal(0, topics["プログラミング言語"].TrendScore, precision: 5);
        Assert.Equal(100, topics["プログラミング言語"].SubtreeTrendScore, precision: 5);
        Assert.Equal(100, topics["プログラミング"].SubtreeTrendScore, precision: 5);
        Assert.Equal(75, topics["python"].SubtreeTrendScore, precision: 5);
    }
}
