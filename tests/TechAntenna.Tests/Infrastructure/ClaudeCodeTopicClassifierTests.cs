using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Topics;

namespace TechAntenna.Tests.Infrastructure;

public class ClaudeCodeTopicClassifierTests
{
    // claude -p --output-format json の応答(structured_output に分類が入る)
    const string Response = """
        {"is_error": false, "structured_output": {"classifications": [
          {"index": 1, "kind": "skip"}
        ]}}
        """;

    [Fact]
    public async Task 上限を超える語はバッチに分けて番号を全体へずらす()
    {
        var runner = StubProcessRunner.Returning(Response);
        var classifier = new ClaudeCodeTopicClassifier(runner, "claude", null, TimeSpan.FromSeconds(1));

        var tags = Enumerable.Range(0, ClaudeCodeTopicClassifier.BatchSize + 1)
            .Select(i => $"tag{i}")
            .ToList();
        var verdicts = await classifier.ClassifyAsync(tags, []);

        // 61 語 → 60 + 1 の2回。各バッチの「1番」が全体の番号へずれて返る
        Assert.Equal(2, runner.CallCount);
        Assert.Equal(2, verdicts.Count);
        Assert.Equal(1, verdicts[0].Index);
        Assert.Equal(ClaudeCodeTopicClassifier.BatchSize + 1, verdicts[1].Index);
    }

    [Fact]
    public async Task 語が無ければ呼び出さない()
    {
        var runner = StubProcessRunner.Returning(Response);
        var classifier = new ClaudeCodeTopicClassifier(runner, "claude", null, TimeSpan.FromSeconds(1));

        Assert.Empty(await classifier.ClassifyAsync([], []));
        Assert.Equal(0, runner.CallCount);
    }
}
