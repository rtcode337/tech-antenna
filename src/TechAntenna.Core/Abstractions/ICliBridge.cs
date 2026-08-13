namespace TechAntenna.Core.Abstractions;

/// <summary>
/// Claude Code の CLI を OpenAI 互換の口に見せるサイドカー(chiezo-bridge)への1回の呼び出し。
///
/// **CLI はこのアプリのイメージに入っていない。** 別コンテナのブリッジへ HTTP で頼み、
/// 認証情報は共有ディレクトリの設定 DB 経由で渡す(<c>BridgeCredentialStore</c>)。
/// プロセスを起動していた頃と違い、イメージに CLI の実体(100MB 超)を積まずに済む。
///
/// テストで差し替えられるよう抽象にしてある(要約・翻訳・分類・ダイジェストが使う)。
/// </summary>
public interface ICliBridge
{
    /// <summary>画面に出す方式名(モデル名まで含む)。</summary>
    string Name { get; }

    /// <summary>
    /// システムプロンプトと本文を渡して、応答の本文を受け取る。
    ///
    /// **返るのはテキスト**(ブリッジは CLI の出力をそのまま返す)。構造化出力の
    /// 仕組みは通らないので、JSON が欲しい呼び出しはプロンプトで指示して読み取る
    /// (<c>ClaudeCodeBatch</c>)。
    /// </summary>
    Task<string> RunAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
