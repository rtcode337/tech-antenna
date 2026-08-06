namespace TechAntenna.Core.Topics;

/// <summary>LLM による未知タグの分類の種別。</summary>
public enum TopicClassificationKind
{
    /// <summary>トピックでない語・判断できない語。平置きのまま(ツリーには入れない)。</summary>
    Skip = 0,

    /// <summary>既存トピックと同じものを指す別表記。正規化でそのトピックへ寄せる。</summary>
    Alias = 1,

    /// <summary>新しいトピック。親(1つ上の粒度)を指定できる。</summary>
    NewTopic = 2,
}

/// <summary>
/// カタログに無いタグを LLM がどう分類したか。**カタログ(topic-catalog.json)とは別に
/// DB へ保存し、読み込み時に合成する** —— JSON は「人が確定させた語彙」、こちらは
/// 「LLM の自動分類」と役割を分け、コンテナを作り直しても分類が消えないようにする。
///
/// 一度分類した語は(Skip も含めて)次回の問い合わせから除くので、
/// 同じ語を毎回 LLM に聞き直さない。
/// </summary>
public class TopicClassification
{
    /// <summary>分類対象のタグ(正規化済みキー)。主キー。</summary>
    public string Tag { get; set; } = "";

    public TopicClassificationKind Kind { get; set; }

    /// <summary><see cref="TopicClassificationKind.Alias"/> のとき、寄せ先トピックのキー。</summary>
    public string? TargetKey { get; set; }

    /// <summary><see cref="TopicClassificationKind.NewTopic"/> のとき、画面に出す正式表記。</summary>
    public string? Display { get; set; }

    /// <summary><see cref="TopicClassificationKind.NewTopic"/> のとき、親トピックのキー。最上位なら null。</summary>
    public string? ParentKey { get; set; }

    public DateTimeOffset ClassifiedAt { get; set; }
}
