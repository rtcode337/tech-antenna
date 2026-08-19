namespace TechAntenna.Core.Abstractions;

/// <summary>
/// 「システムプロンプトと本文を渡してテキストを受け取る」1回の呼び出し。
///
/// 実装は2つあり、どちらも同じ口にしてある —— 同梱の Claude Code の CLI を
/// プロセスとして起動するもの(<c>ClaudeCodeCliBridge</c>)と、Chiezo(LAN 内の
/// 知識サーバー)越しに相手を選ぶもの(<c>ChiezoAiBridge</c>)。要約・翻訳・分類・
/// ダイジェストはこの抽象しか知らないので、相手が変わっても同じ組み立てが使える。
///
/// テストで差し替えられるよう抽象にしてある。
/// </summary>
public interface ICliBridge
{
    /// <summary>画面に出す方式名(モデル名まで含む)。</summary>
    string Name { get; }

    /// <summary>
    /// システムプロンプトと本文を渡して、応答の本文を受け取る。
    ///
    /// 返るのはテキスト。CLI には構造化出力(`--json-schema`)もあるが Chiezo 側には
    /// 無いので、JSON の受け取り方を1本に保つためどちらもプロンプトで指示して読み取る
    /// (<c>ClaudeCodeBatch</c>。読めなければ言い直させる経路もそこにある)。
    /// </summary>
    Task<string> RunAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
