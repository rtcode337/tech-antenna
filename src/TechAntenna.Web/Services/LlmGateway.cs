using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Infrastructure.Bridge;
using TechAntenna.Infrastructure.Chiezo;
using TechAntenna.Infrastructure.Summarization;
using TechAntenna.Infrastructure.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// LLM を使う機能(要約・翻訳・語彙の仕分け・今日のサマリー)の実装を、
/// **実行のたびにキーの状態から選ぶ**。かつては起動時に環境変数を見て DI 登録を
/// 分岐していたが、それだと画面からキーを設定しても再起動するまで効かない。
///
/// 方式の優先: **Chiezo(URL を設定し、画面でメインの AI を選んである)** >
/// Claude Code のトークン(サブスクの枠)> Anthropic API キー(従量課金)>
/// どれも無ければ null(ボタンは disabled で出る)。
///
/// **Chiezo を先に見るのは、そこが「相手を選べる」唯一の経路だから** —— わざわざ選んだ
/// 相手より、同居のサイドカーを優先する理由が無い。URL が無い・メインを選んでいない
/// 環境では今までどおり動く。
///
/// 組み立ては <see cref="ApiCredentials.Version"/> が変わったときだけ ——
/// キーが変わらない限り同じインスタンスを返し続ける(Anthropic のクライアントや
/// ブリッジのクライアントを呼び出しごとに作り直さない)。
///
/// **Claude Code のトークンは、組み立てのときに共有ディレクトリへ書き出す**
/// (<see cref="BridgeCredentialStore"/>)。CLI を動かすのは別コンテナ(chiezo-bridge)で、
/// そこはこのアプリのプロセスの環境変数を見られないため。版数が変わったときだけ書くので、
/// 画面で入れ替えた直後に反映され、ブリッジの再起動も要らない。
/// </summary>
public class LlmGateway(
    ApiCredentials credentials,
    IHttpClientFactory httpClientFactory,
    IOptions<ClaudeCodeOptions> claudeCodeOptions,
    IOptions<AnthropicOptions> anthropicOptions,
    ChiezoAi chiezo,
    TimeProvider timeProvider,
    ILogger<LlmGateway> logger)
{
    /// <summary>
    /// Claude Code のトークンの設定キー。**CLI が読む環境変数と同じ名前**にしてある ——
    /// 画面の説明とブリッジ側のフォールバック(<c>CLAUDE_CODE_OAUTH_TOKEN</c>)で
    /// 同じ言葉を使えるようにするため。
    /// </summary>
    public const string ClaudeCodeTokenName = "CLAUDE_CODE_OAUTH_TOKEN";

    /// <summary>
    /// Chiezo を使っていないときの生成者のキー。**相手を選べない経路はこれ 1 つ**
    /// (Claude Code / Anthropic API のどちらでも、同時に走るのは 1 本)。
    /// </summary>
    public const string DefaultGeneratorKey = "default";

    /// <summary>Anthropic API キーの設定キー。</summary>
    public const string AnthropicApiKeyName = "Anthropic:ApiKey";

    record Built(
        int Version,
        ISummarizer? Summarizer,
        ITitleTranslator? TitleTranslator,
        ITopicClassifier? Classifier,
        ITopicDescriber? Describer,
        ITopicMergeAdvisor? MergeAdvisor,
        IDigestComposer? DigestComposer,
        IReadOnlyList<DigestGenerator> DigestGenerators);

    readonly object _gate = new();
    Built? _built;

    public virtual ISummarizer? Summarizer => Current().Summarizer;

    public virtual ITitleTranslator? TitleTranslator => Current().TitleTranslator;

    public virtual ITopicClassifier? Classifier => Current().Classifier;

    public virtual ITopicDescriber? Describer => Current().Describer;

    public virtual ITopicMergeAdvisor? MergeAdvisor => Current().MergeAdvisor;

    public virtual IDigestComposer? DigestComposer => Current().DigestComposer;

    /// <summary>
    /// 今日のサマリーを書かせる相手(**メインが先頭**)。Chiezo でサブを選んでいれば、
    /// その相手ぶんも並ぶ —— ホームで読み比べるため。
    /// </summary>
    public virtual IReadOnlyList<DigestGenerator> DigestGenerators => Current().DigestGenerators;

    /// <summary>いずれかの方式が使えるか。</summary>
    public virtual bool IsConfigured => Current().Summarizer is not null;

    /// <summary>LLM ジョブのボタンに出す、未設定の理由(全ジョブで共通)。</summary>
    public const string NotConfiguredReason =
        "Claude Code のトークンか Anthropic API キーが未設定のため、実行できません。";

    Built Current()
    {
        var version = credentials.Version;
        var built = _built;
        if (built?.Version == version)
        {
            return built;
        }

        lock (_gate)
        {
            if (_built?.Version != version)
            {
                _built = Build(version);
            }

            return _built;
        }
    }

    Built Build(int version)
    {
        if (BuildFromChiezo(version) is { } fromChiezo)
        {
            return fromChiezo;
        }

        var claudeCode = claudeCodeOptions.Value;
        var token = credentials.Get(ClaudeCodeTokenName);
        // **トークンを消したときも書く。** 書かないと、共有ディレクトリに残った古い
        // トークンでブリッジが動き続ける(Anthropic API へ切り替えたつもりで切り替わらない)
        WriteBridgeCredential(claudeCode.StateDirectory, token);

        if (token is not null)
        {
            var model = string.IsNullOrWhiteSpace(claudeCode.Model) ? null : claudeCode.Model;
            var bridge = new CliBridgeClient(
                httpClientFactory,
                claudeCode.BridgeUrl,
                model,
                TimeSpan.FromSeconds(claudeCode.TimeoutSeconds));
            var classifier = new ClaudeCodeTopicClassifier(bridge);
            return new Built(
                version,
                new ClaudeCodeSummarizer(bridge),
                new ClaudeCodeTitleTranslator(bridge),
                classifier,
                classifier,
                classifier,
                new ClaudeCodeDigestComposer(bridge, timeProvider),
                [new DigestGenerator(DefaultGeneratorKey, bridge.Name, true,
                    new ClaudeCodeDigestComposer(bridge, timeProvider))]);
        }

        var apiKey = credentials.Get(AnthropicApiKeyName);
        if (apiKey is not null)
        {
            var anthropic = anthropicOptions.Value;
            var classifier = new AnthropicTopicClassifier(apiKey, anthropic.Model);
            return new Built(
                version,
                new AnthropicSummarizer(apiKey, anthropic.Model),
                new AnthropicTitleTranslator(apiKey, anthropic.Model),
                classifier,
                classifier,
                classifier,
                new AnthropicDigestComposer(apiKey, anthropic.Model, timeProvider),
                [new DigestGenerator(DefaultGeneratorKey, "Anthropic API", true,
                    new AnthropicDigestComposer(apiKey, anthropic.Model, timeProvider))]);
        }

        return new Built(version, null, null, null, null, null, null, []);
    }

    /// <summary>
    /// Chiezo 経由の実装。URL が未設定・メインを選んでいなければ null(呼び出し側が次の方式へ)。
    /// **サブはダイジェストにだけ効かせる**(要約や翻訳まで相手の数だけ走らせない)。
    /// </summary>
    Built? BuildFromChiezo(int version)
    {
        var config = AiSettings.Load(credentials);
        if (chiezo.Client() is not { } client || config.Main is not { } main)
        {
            return null;
        }
        var mainBridge = new ChiezoAiBridge(client, main.ToSelection());
        var classifier = new ClaudeCodeTopicClassifier(mainBridge);

        var generators = config.All()
            .Select(choice => new DigestGenerator(
                choice.Key,
                choice.ToSelection().DisplayName,
                choice.Backend == main.Backend,
                new ClaudeCodeDigestComposer(new ChiezoAiBridge(client, choice.ToSelection()), timeProvider)))
            .ToList();

        return new Built(
            version,
            new ClaudeCodeSummarizer(mainBridge),
            new ClaudeCodeTitleTranslator(mainBridge),
            classifier,
            classifier,
            classifier,
            new ClaudeCodeDigestComposer(mainBridge, timeProvider),
            generators);
    }

    /// <summary>
    /// ブリッジが読む設定 DB を書き直す。**書けなくてもここでは止めない** ——
    /// 止めるとキーの保存そのものが失敗したように見えるうえ、前回書けていれば
    /// ブリッジは動く。実際に効いていないときは、ジョブの実行がブリッジの
    /// 401(認証情報が未登録)で失敗するので、そちらに理由が出る。
    /// </summary>
    void WriteBridgeCredential(string stateDirectory, string? token)
    {
        // **一度も使っていない環境には作らない。** Anthropic API だけで運用しているなら
        // 共有ディレクトリごと不要なので、消すものが無いとき(トークンも設定 DB も無い)は触らない
        if (token is null && !File.Exists(BridgeCredentialStore.PathIn(stateDirectory)))
        {
            return;
        }

        try
        {
            BridgeCredentialStore.Write(stateDirectory, token, timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CLI ブリッジと共有する設定 DB({Path})を書けません。"
                + "ブリッジは古いトークンのまま動きます",
                BridgeCredentialStore.PathIn(stateDirectory));
        }
    }
}
