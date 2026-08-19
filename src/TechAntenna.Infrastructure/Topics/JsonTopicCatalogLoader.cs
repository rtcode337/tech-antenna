using System.Text.Json;
using System.Text.Json.Serialization;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Topics;

/// <summary>
/// JSON ファイルからトピックカタログを読む。
///
/// 読めなくても起動は止めない(空のカタログで動く)。カタログが無いと別名がまとまらないだけで、
/// 収集も表示も成立するため。個人運用で、ファイルの置き忘れで画面ごと落ちるほうが困る。
/// </summary>
public static class JsonTopicCatalogLoader
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TopicCatalog Load(string path)
    {
        if (!File.Exists(path))
        {
            return TopicCatalog.Empty;
        }

        var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Options);

        return new TopicCatalog(
            (file?.Topics ?? [])
                .Where(topic => !string.IsNullOrWhiteSpace(topic.Display))
                .Select(topic => new TopicCatalogEntry(
                    topic.Display.Trim(), topic.Aliases ?? [], topic.Parent, topic.Description?.Trim()))
                .ToList());
    }

    class CatalogFile
    {
        public List<CatalogTopic>? Topics { get; set; }
    }

    class CatalogTopic
    {
        public string Display { get; set; } = "";

        public List<string>? Aliases { get; set; }

        public string? Parent { get; set; }

        /// <summary>一言説明(任意)。書いておけば LLM の説明より優先される。</summary>
        public string? Description { get; set; }
    }
}
