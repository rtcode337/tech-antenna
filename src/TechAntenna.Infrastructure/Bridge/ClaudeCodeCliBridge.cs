using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Bridge;

/// <summary>
/// Claude Code の CLI(`claude -p`)をこのプロセスから直に起動する実装。
///
/// サイドカー(chiezo-bridge)をやめて同梱に戻した。分けていたのは CLI の実体が
/// 100MB 超あってイメージが倍近くなるためだったが、公開リポジトリになって
/// イメージの容量を気にする理由が薄れた。同梱に戻すと得るものが2つある ——
/// 別コンテナを立てなくても要約が動く(公開したものを試す人の手数が減る)、
/// 認証情報を共有ディレクトリの設定 DB 経由で渡す仕掛けが要らなくなる
/// (画面で入れたトークンは子プロセスの環境変数として渡す)。
///
/// ここに集めてあるのは、踏まないと分からない作法:
/// - プロンプトは引数ではなく標準入力で渡す。Linux の単一引数の長さ上限
///   (MAX_ARG_STRLEN = 128KiB)を、記事をまとめると容易に超えて E2BIG で落ちる
/// - `--bare` は使えない。keychain と OAuth の読み取りを飛ばすため
///   `CLAUDE_CODE_OAUTH_TOKEN` が効かなくなる
/// - 道具は全部禁じる。許すと1ターンを道具の呼び出しに使い、結果が返らないことがある
/// - 失敗の詳細は stderr に出ないことがある(CLI が stdout に書く)ので、両方を見る
///
/// 応答はテキストで受ける(`--output-format text`)。CLI には構造化出力
/// (`--json-schema`)もあるが、Chiezo 越しの相手(<c>ChiezoAiBridge</c>)には無い ——
/// JSON の受け取り方を1本に保つため、どちらもプロンプトで指示して読み取る
/// (<c>ClaudeCodeBatch</c>。読めなければ言い直させる経路もそこにある)。
/// </summary>
/// <param name="model">
/// 使うモデル。null なら CLI の既定に任せる(既定は重いモデルになりがちなので、
/// 設定で `claude-sonnet-5` を明示してある)。
/// </param>
public class ClaudeCodeCliBridge(
    IProcessRunner processRunner,
    string executablePath,
    string? model,
    TimeSpan timeout) : ICliBridge
{
    /// <summary>
    /// 道具の禁止一覧。名前で並べる —— CLI に「全部禁止」の指定が無いため
    /// (増えた道具は次に踏んだときここへ足す)。
    /// </summary>
    const string DisallowedTools =
        "Bash,Read,Edit,Write,Glob,Grep,WebSearch,WebFetch,Task,TodoWrite," +
        "NotebookEdit,BashOutput,KillShell,SlashCommand,ExitPlanMode";

    /// <summary>
    /// 道具を引く往復の上限。道具は禁じてあるので 1 回で返るが、CLI が内部で
    /// 1 往復使う場合に備えて 2 にしてある(使わなければ増えない)。
    /// </summary>
    const int MaxTurns = 2;

    // モデル名まで画面に出す —— どのモデルがサブスクの枠を使っているか見えるようにする。
    // model が null(CLI の既定に任せる)ときは、既定が何かこちらから分からないので付けない
    public string Name => model is null ? "Claude Code" : $"Claude Code / {model}";

    public async Task<string> RunAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-p",
            "--max-turns", MaxTurns.ToString(),
            "--system-prompt", systemPrompt,
            "--output-format", "text",
            "--disallowed-tools", DisallowedTools,
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model);
        }

        var result = await processRunner.RunAsync(
            executablePath, arguments, userPrompt, timeout, cancellationToken);

        if (result.TimedOut)
        {
            throw new TimeoutException(
                $"claude が {timeout.TotalSeconds:0} 秒で終わらなかった。");
        }

        if (result.ExitCode != 0)
        {
            // 理由をそのまま載せる。認証切れ・モデル名の間違いはここに出る
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException(
                $"claude が終了コード {result.ExitCode} で失敗した: {Excerpt(detail)}");
        }

        return result.StandardOutput;
    }

    /// <summary>例外メッセージにそのまま載せられる長さに切る。</summary>
    static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}
