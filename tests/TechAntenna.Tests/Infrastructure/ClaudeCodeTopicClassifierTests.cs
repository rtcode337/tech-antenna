using TechAntenna.Infrastructure.Topics;

namespace TechAntenna.Tests.Infrastructure;

public class ClaudeCodeTopicClassifierTests
{
    // ブリッジが返す本文(分類の JSON)
    const string Response = """{"classifications": [{"index": 1, "kind": "skip"}]}""";

    [Fact]
    public async Task 上限を超える語はバッチに分けて番号を全体へずらす()
    {
        var bridge = new StubCliBridge(Response);
        var classifier = new ClaudeCodeTopicClassifier(bridge);

        var tags = Enumerable.Range(0, ClaudeCodeTopicClassifier.BatchSize + 1)
            .Select(i => $"tag{i}")
            .ToList();
        var verdicts = await classifier.ClassifyAsync(tags, []);

        // 61 語 → 60 + 1 の2回。各バッチの「1番」が全体の番号へずれて返る
        Assert.Equal(2, bridge.CallCount);
        Assert.Equal(2, verdicts.Count);
        Assert.Equal(1, verdicts[0].Index);
        Assert.Equal(ClaudeCodeTopicClassifier.BatchSize + 1, verdicts[1].Index);
    }

    [Fact]
    public async Task 語が無ければ呼び出さない()
    {
        var bridge = new StubCliBridge(Response);
        var classifier = new ClaudeCodeTopicClassifier(bridge);

        Assert.Empty(await classifier.ClassifyAsync([], []));
        Assert.Equal(0, bridge.CallCount);
    }
}
