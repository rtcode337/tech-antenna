using System.Text.Json;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// <see cref="ICliBridge"/> を1回呼んで、番号付きの結果を受け取る。要約・翻訳・分類・ダイジェストで共有する。
///
/// ここに集めてあるのは、踏まないと分からない作法:
/// - スキーマはプロンプトで指示する。CLI を直接起動していた頃は `--json-schema` で
///   構造化出力を強制できたが、ブリッジは OpenAI 互換の口(テキスト応答)なので載せる場所が
///   無い。代わりに「JSON だけを返す」と書いて渡し、応答からは JSON の部分だけを取り出す
///   (<see cref="ClaudeCodeResponseParser"/>)—— 崩れた応答は例外にして、ジョブ側が次の
///   巡回で引き直す
/// - 番号と値の対応で返させる(記事の Id を LLM に写させない)
/// - 長さの上限を気にしなくてよい。CLI に直接渡していた頃は Linux の単一引数の上限
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

    public static Task<IReadOnlyList<ClaudeCodeResponseParser.Entry>> RunAsync(
        ICliBridge bridge,
        string systemPrompt,
        string arrayName,
        string valueName,
        string input,
        CancellationToken cancellationToken) =>
        RunJsonAsync(
            bridge,
            systemPrompt,
            Schema(arrayName, valueName),
            input,
            root => ClaudeCodeResponseParser.ReadEntries(root, arrayName, valueName),
            cancellationToken);

    /// <summary>
    /// ブリッジを呼んで、応答の JSON を <paramref name="read"/> に渡す。番号+文字列の形に
    /// 収まらないスキーマ(トピック分類・ダイジェスト)もこれで読む。
    ///
    /// 読めなかったら1回だけ言い直す。スキーマを強制できない以上、
    /// 「材料が少ないのでダイジェストを作れません」のような説明文で返ってくることがある
    /// (実測)。2回目も駄目ならそのまま投げて、ジョブ側が失敗として記録する。
    /// </summary>
    public static async Task<T> RunJsonAsync<T>(
        ICliBridge bridge,
        string systemPrompt,
        string schemaJson,
        string input,
        Func<JsonElement, T> read,
        CancellationToken cancellationToken)
    {
        var system = WithSchema(systemPrompt, schemaJson);
        var text = await bridge.RunAsync(system, input, cancellationToken);
        try
        {
            return ClaudeCodeResponseParser.ReadJson(text, read);
        }
        catch (FormatException)
        {
            // 1 回目の応答は例外のメッセージに載るので、ここではログを足さない
        }

        var retry = await bridge.RunAsync(system + RetryNote, input, cancellationToken);
        return ClaudeCodeResponseParser.ReadJson(retry, read);
    }

    /// <summary>言い直しのときに足す指示。断る余地を残さないのが要点。</summary>
    const string RetryNote =
        "\n\n**前回の応答は JSON ではなかった。** 説明・断り書き・前置きを書かず、"
        + "上記スキーマの JSON だけを返す。材料が少ないときも、その範囲で組み立てて JSON で返す"
        + "(作れない理由を文章で書かない)。";

    /// <summary>
    /// スキーマの指示をシステムプロンプトの末尾に足す。コードフェンスを禁じておく ——
    /// 付いていても読み取り側が外すが、本文のつもりの説明が前後に混ざるのを減らせる。
    /// </summary>
    public static string WithSchema(string systemPrompt, string schemaJson) =>
        systemPrompt
        + "\n\n出力は次の JSON スキーマに従った JSON だけにする。"
        + "前置き・説明・コードフェンス(```)を付けない。\n"
        + schemaJson;
}
