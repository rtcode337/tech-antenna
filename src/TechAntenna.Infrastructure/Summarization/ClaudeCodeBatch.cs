using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// CLI ブリッジを1回呼んで、番号付きの結果を受け取る。要約・翻訳・分類・ダイジェストで共有する。
///
/// **ここに集めてあるのは、踏まないと分からない作法**:
/// - **スキーマはプロンプトで指示する**。CLI を直接起動していた頃は `--json-schema` で
///   構造化出力を強制できたが、ブリッジは OpenAI 互換の口(テキスト応答)なので載せる場所が
///   無い。代わりに「JSON だけを返す」と書いて渡し、応答からは JSON の部分だけを取り出す
///   (<see cref="ClaudeCodeResponseParser"/>)—— 崩れた応答は例外にして、ジョブ側が次の
///   巡回で引き直す
/// - **番号と値の対応で返させる**(記事の Id を LLM に写させない)
/// - **長さの上限を気にしなくてよい**。CLI に直接渡していた頃は Linux の単一引数の上限
///   (MAX_ARG_STRLEN = 128KiB)を避けて標準入力に流していたが、HTTP の本文にはその制限が無い
/// </summary>
public static class ClaudeCodeBatch
{
    /// <summary>番号と値の配列を返させる JSON Schema を組み立てる。</summary>
    // 波括弧だらけなので、生文字列の補間ではなく素直に連結する
    public static string Schema(string arrayName, string valueName) =>
        "{\"type\":\"object\",\"properties\":{\"" + arrayName
        + "\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":"
        + "{\"index\":{\"type\":\"integer\"},\"" + valueName + "\":{\"type\":\"string\"}},"
        + "\"required\":[\"index\",\"" + valueName + "\"]}}},\"required\":[\"" + arrayName + "\"]}";

    public static async Task<IReadOnlyList<ClaudeCodeResponseParser.Entry>> RunAsync(
        ICliBridge bridge,
        string systemPrompt,
        string arrayName,
        string valueName,
        string input,
        CancellationToken cancellationToken)
    {
        var text = await RunRawAsync(
            bridge, systemPrompt, Schema(arrayName, valueName), input, cancellationToken);

        return ClaudeCodeResponseParser.Parse(text, arrayName, valueName);
    }

    /// <summary>
    /// ブリッジを1回呼んで応答の本文をそのまま返す。番号+文字列の形に収まらないスキーマ
    /// (トピック分類など)はこちらを使い、呼び出し側で
    /// <see cref="ClaudeCodeResponseParser.ReadJson{T}"/> する。
    /// </summary>
    public static Task<string> RunRawAsync(
        ICliBridge bridge,
        string systemPrompt,
        string schemaJson,
        string input,
        CancellationToken cancellationToken) =>
        bridge.RunAsync(WithSchema(systemPrompt, schemaJson), input, cancellationToken);

    /// <summary>
    /// スキーマの指示をシステムプロンプトの末尾に足す。**コードフェンスを禁じておく** ——
    /// 付いていても読み取り側が外すが、本文のつもりの説明が前後に混ざるのを減らせる。
    /// </summary>
    public static string WithSchema(string systemPrompt, string schemaJson) =>
        systemPrompt
        + "\n\n出力は次の JSON スキーマに従った JSON だけにする。"
        + "前置き・説明・コードフェンス(```)を付けない。\n"
        + schemaJson;
}
