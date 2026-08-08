using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Topics;

/// <summary>
/// 持ち出し・取り込みファイル1つ分。**語彙(トピック)とタグの仕分けだけを運ぶ。**
///
/// 件数・話題度・最初に見た時刻といった<b>観測は入れない</b> —— あれは
/// 「その環境が集めたデータ」の話で、持ち込むと取り込み先の実データと食い違う
/// (件数は再編成が集め直す)。運ぶのは<b>人と LLM が決めた仕分け</b>だけ。
/// </summary>
public record TopicExportFile
{
    /// <summary>ファイルを読んだ人向けの説明。取り込みでは読み飛ばす。</summary>
    [JsonPropertyName("$comment")]
    public IReadOnlyList<string>? Comment { get; init; }

    public DateTimeOffset? ExportedAt { get; init; }

    public IReadOnlyList<TopicExportEntry> Topics { get; init; } = [];

    public IReadOnlyList<TagExportEntry> Tags { get; init; } = [];
}

/// <summary>語彙1件。</summary>
/// <param name="Key">正規化済みキー(`生成ai`)。</param>
/// <param name="Display">正式表記(`生成AI`)。</param>
/// <param name="Parent">1つ上の粒度のキー。最上位なら null。</param>
/// <param name="English">英語圏の収集元へ投げる検索語。</param>
/// <param name="Description">用語の一言説明。</param>
/// <param name="DecidedBy">この語彙の出どころ(シード / LLM / 人の手直し)。</param>
/// <param name="Selected">
/// 持ち出した環境で収集対象に選ばれていたか。**取り込みでは既定で使わない**
/// (収集キーワードが黙って変わるのを避けるため。取り込む側の画面で明示的に指定したときだけ効く)。
/// </param>
public record TopicExportEntry(
    string Key,
    string Display,
    string? Parent = null,
    string? English = null,
    string? Description = null,
    DecidedBy DecidedBy = DecidedBy.None,
    bool Selected = false);

/// <summary>タグ1語の仕分け。**未仕分け(Pending)は持ち出さない**(何も決まっていないため)。</summary>
/// <param name="Key">正規化済みタグ。</param>
/// <param name="Status">仕分けた状態。</param>
/// <param name="TopicKey">Promoted なら自分自身、Alias なら寄せ先。</param>
/// <param name="DecidedBy">誰が決めたか。</param>
/// <param name="DecidedAt">決めた時刻。**持ち出し元の時刻をそのまま運ぶ** ——
/// 「同じ実行で付けた分類は時刻が揃う」という手がかりを壊さないため。</param>
/// <param name="RetryAfter">Unresolved をもう一度聞いてよくなる時刻。</param>
public record TagExportEntry(
    string Key,
    TagStatus Status,
    string? TopicKey = null,
    DecidedBy DecidedBy = DecidedBy.None,
    DateTimeOffset? DecidedAt = null,
    DateTimeOffset? RetryAfter = null);

/// <summary>
/// 持ち出しファイルの JSON 変換。
///
/// **人が読んで差分が取れる形にする**(整形あり・列挙は名前・日本語はエスケープしない)——
/// 環境間で運ぶだけでなく、git に置いて中身を見比べたり手で直したりできるようにするため。
/// </summary>
public static class TopicExportJson
{
    static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // 日本語をそのまま出す(\uXXXX に潰すと人が読めない)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(TopicExportFile file) =>
        JsonSerializer.Serialize(file, WriteOptions);

    /// <summary>
    /// 読み込む。**壊れたファイルは例外を投げる**(取り込み側が画面に理由を出す)——
    /// 黙って空として扱うと、「取り込んだのに何も増えない」で原因が分からなくなる。
    /// </summary>
    public static TopicExportFile Deserialize(string json) =>
        JsonSerializer.Deserialize<TopicExportFile>(json, ReadOptions)
        ?? throw new JsonException("ファイルの中身が空です。");
}
