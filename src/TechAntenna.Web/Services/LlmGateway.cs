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
/// **画面(設定 → 外部連携の一番上)で選んだメインに従う。** 選べるのは Chiezo の相手と、
/// Claude Code(サブスクの枠)・Anthropic API(従量課金)の3種類。
/// **選んでいない(または選んだ相手のキーが消えた)ときは従来の優先順** ——
/// Claude Code のトークン > Anthropic API キー > どれも無ければ null
/// (ボタンは disabled で出る)。
///
/// かつては「Chiezo にメインを選んだかどうか」で経路が決まり、選ばないときだけ上の
/// 優先順に落ちていた。そのため**キーを両方入れてある環境で Anthropic API を選ぶ手段が
/// 無かった**(トークンを消すしかなかった)ので、2つも選択肢として並べてある。
///
/// **サブは Chiezo の相手だけ**(読み比べのための経路)。メインが Claude Code や
/// Anthropic API でも、Chiezo のサブは今日のサマリーに並ぶ。
///
/// 組み立ては <see cref="ApiCredentials.Version"/> が変わったときだけ ——
/// キーが変わらない限り同じインスタンスを返し続ける(Anthropic のクライアントや
/// ブリッジのクライアントを呼び出しごとに作り直さない)。
///
/// **Claude Code の CLI はこのイメージに同梱**してあり、プロセスとして起動する
/// (<see cref="ClaudeCodeCliBridge"/>)。画面で入れたトークンは<b>子プロセスの環境変数</b>
/// として渡す(<c>SystemProcessRunner</c> の環境提供。このプロセス自身の環境変数は変えない)。
/// かつては別コンテナのブリッジへ HTTP で頼み、認証情報を共有ディレクトリの設定 DB 経由で
/// 渡していたが、公開リポジトリになってイメージの容量を気にする理由が薄れたので同梱へ戻した。
/// </summary>
public class LlmGateway(
    ApiCredentials credentials,
    IProcessRunner processRunner,
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
        var config = AiSettings.Load(credentials);
        var claudeCode = claudeCodeOptions.Value;
        var token = credentials.Get(ClaudeCodeTokenName);
        var apiKey = credentials.Get(AnthropicApiKeyName);
        var client = chiezo.Client();

        // **画面で選んだメインに従う。** 選んだ相手のキーが無い(消した)ときは下の優先順へ落ちる
        switch (config.Main)
        {
            case { } main when main.Backend == AiSettings.ClaudeCodeBackend && token is not null:
                return FromBridge(version, NewCliBridge(claudeCode), main.Key, config, client);

            case { } main when main.Backend == AiSettings.AnthropicBackend && apiKey is not null:
                return FromAnthropic(version, apiKey, main.Key, config, client);

            case { } main when !AiSettings.IsLocal(main.Backend) && client is not null:
                return FromBridge(
                    version, new ChiezoAiBridge(client, main.ToSelection()), main.Key, config, client);
        }

        // 選んでいない・選んだ相手が使えないときは従来の優先順(トークン > API キー)
        if (token is not null)
        {
            return FromBridge(
                version, NewCliBridge(claudeCode), DefaultGeneratorKey, config, client);
        }

        if (apiKey is not null)
        {
            return FromAnthropic(version, apiKey, DefaultGeneratorKey, config, client);
        }

        return new Built(version, null, null, null, null, null, null, []);
    }

    ClaudeCodeCliBridge NewCliBridge(ClaudeCodeOptions options) =>
        // トークンは子プロセスの環境変数として渡る(IProcessRunner の登録。Program.cs)ので、
        // ここが知っているのは実行ファイル・モデル・上限秒数だけ
        new(processRunner,
            options.ExecutablePath,
            string.IsNullOrWhiteSpace(options.Model) ? null : options.Model,
            TimeSpan.FromSeconds(options.TimeoutSeconds));

    /// <summary>
    /// <see cref="ICliBridge"/> 越しの実装(同梱の CLI と Chiezo は**同じ口**なので、
    /// <c>ICliBridge</c> の実装を差し替えるだけで同じ組み立てが使える)。
    /// </summary>
    Built FromBridge(
        int version, ICliBridge bridge, string mainKey, AiConfig config, ChiezoAiClient? client)
    {
        var classifier = new ClaudeCodeTopicClassifier(bridge);
        return new Built(
            version,
            new ClaudeCodeSummarizer(bridge),
            new ClaudeCodeTitleTranslator(bridge),
            classifier,
            classifier,
            classifier,
            new ClaudeCodeDigestComposer(bridge, timeProvider),
            Generators(mainKey, bridge.Name, new ClaudeCodeDigestComposer(bridge, timeProvider),
                config, client));
    }

    Built FromAnthropic(
        int version, string apiKey, string mainKey, AiConfig config, ChiezoAiClient? client)
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
            Generators(mainKey, "Anthropic API",
                new AnthropicDigestComposer(apiKey, anthropic.Model, timeProvider), config, client));
    }

    /// <summary>
    /// 今日のサマリーを書かせる相手(**メインが先頭**)。**サブは Chiezo の相手だけ** ——
    /// 読み比べのための経路で、Chiezo でなければ相手を名指しできない。
    /// メインと同じキーになるサブは落とす(同じ相手に2回書かせても比べる意味が無い)。
    /// </summary>
    IReadOnlyList<DigestGenerator> Generators(
        string mainKey, string mainName, IDigestComposer mainComposer,
        AiConfig config, ChiezoAiClient? client)
    {
        var generators = new List<DigestGenerator>
        {
            new(mainKey, mainName, true, mainComposer),
        };

        if (client is null)
        {
            return generators;
        }

        foreach (var sub in config.Subs.Where(sub => !AiSettings.IsLocal(sub.Backend)))
        {
            if (sub.Key == mainKey)
            {
                continue;
            }

            generators.Add(new DigestGenerator(
                sub.Key,
                sub.ToSelection().DisplayName,
                false,
                new ClaudeCodeDigestComposer(new ChiezoAiBridge(client, sub.ToSelection()), timeProvider)));
        }

        return generators;
    }
}
