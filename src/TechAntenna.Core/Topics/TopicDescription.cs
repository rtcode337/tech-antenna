namespace TechAntenna.Core.Topics;

/// <summary>
/// LLM が付けたトピックの一言説明。**カタログ(`topic-catalog.json`)とは別に DB へ保存し、
/// 読み込み時に合成する** —— 分類(<see cref="TopicClassification"/>)と同じ役割分担で、
/// JSON は「人が書いた説明」、こちらは「LLM が埋めた説明」。同じキーなら JSON が勝つ。
///
/// **1 語につき 1 回だけ聞く。** 保存しないと再編成のたびに同じ語を聞き直して LLM の枠を
/// 無駄にする(分類で Skip / Unknown を保存しているのと同じ理由)。
/// </summary>
public class TopicDescription
{
    /// <summary>トピックのキー(正規化済み)。主キー。</summary>
    public string Key { get; set; } = "";

    /// <summary>一言説明(1〜2文)。</summary>
    public string Text { get; set; } = "";

    public DateTimeOffset DescribedAt { get; set; }
}
