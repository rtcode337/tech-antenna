using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Infrastructure;
using TechAntenna.Infrastructure.Summarization;
using TechAntenna.Infrastructure.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// LLM を使う機能(要約・翻訳・語彙の仕分け・今日のサマリー)の実装を、
/// **実行のたびにキーの状態から選ぶ**。かつては起動時に環境変数を見て DI 登録を
/// 分岐していたが、それだと画面からキーを設定しても再起動するまで効かない。
///
/// 方式の優先は従来と同じ: Claude Code のトークン(サブスクの枠)>
/// Anthropic API キー(従量課金)> どちらも無ければ null(ボタンは disabled で出る)。
///
/// 組み立ては <see cref="ApiCredentials.Version"/> が変わったときだけ ——
/// キーが変わらない限り同じインスタンスを返し続ける(Anthropic のクライアントや
/// プロセスランナーを呼び出しごとに作り直さない)。
/// </summary>
public class LlmGateway(
    ApiCredentials credentials,
    IOptions<ClaudeCodeOptions> claudeCodeOptions,
    IOptions<AnthropicOptions> anthropicOptions,
    TimeProvider timeProvider)
{
    /// <summary>Claude Code の CLI が読むトークンの環境変数名。</summary>
    public const string ClaudeCodeTokenName = "CLAUDE_CODE_OAUTH_TOKEN";

    /// <summary>Anthropic API キーの設定キー。</summary>
    public const string AnthropicApiKeyName = "Anthropic:ApiKey";

    record Built(
        int Version,
        ISummarizer? Summarizer,
        ITitleTranslator? TitleTranslator,
        ITopicClassifier? Classifier,
        ITopicDescriber? Describer,
        ITopicMergeAdvisor? MergeAdvisor,
        IDigestComposer? DigestComposer);

    readonly object _gate = new();
    Built? _built;

    public virtual ISummarizer? Summarizer => Current().Summarizer;

    public virtual ITitleTranslator? TitleTranslator => Current().TitleTranslator;

    public virtual ITopicClassifier? Classifier => Current().Classifier;

    public virtual ITopicDescriber? Describer => Current().Describer;

    public virtual ITopicMergeAdvisor? MergeAdvisor => Current().MergeAdvisor;

    public virtual IDigestComposer? DigestComposer => Current().DigestComposer;

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
        var token = credentials.Get(ClaudeCodeTokenName);
        if (token is not null)
        {
            var claudeCode = claudeCodeOptions.Value;
            var model = string.IsNullOrWhiteSpace(claudeCode.Model) ? null : claudeCode.Model;
            var timeout = TimeSpan.FromSeconds(claudeCode.TimeoutSeconds);
            // トークンは CLI が環境変数から読む。画面で設定した値も同じ経路で子プロセスに
            // 渡す(アプリ自身の環境変数は書き換えない —— 他と同じく再起動なしで差し替えるため)
            var processRunner = new SystemProcessRunner(
                () => new Dictionary<string, string> { [ClaudeCodeTokenName] = token });
            var classifier = new ClaudeCodeTopicClassifier(
                processRunner, claudeCode.ExecutablePath, model, timeout);
            return new Built(
                version,
                new ClaudeCodeSummarizer(processRunner, claudeCode.ExecutablePath, model, timeout),
                new ClaudeCodeTitleTranslator(processRunner, claudeCode.ExecutablePath, model, timeout),
                classifier,
                classifier,
                classifier,
                new ClaudeCodeDigestComposer(
                    processRunner, claudeCode.ExecutablePath, model, timeout, timeProvider));
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
                new AnthropicDigestComposer(apiKey, anthropic.Model, timeProvider));
        }

        return new Built(version, null, null, null, null, null, null);
    }
}
