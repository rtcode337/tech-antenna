using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

/// <summary>
/// テストで LLM 実装(スタブ)を差し込むための LlmGateway。
/// 本物はキーの状態から実装を組み立てるが、こちらは渡されたものをそのまま返す。
/// </summary>
public class StubLlmGateway(
    ISummarizer? summarizer = null,
    ITitleTranslator? titleTranslator = null,
    ITopicClassifier? classifier = null,
    ITopicDescriber? describer = null,
    ITopicMergeAdvisor? mergeAdvisor = null,
    IDigestComposer? digestComposer = null)
    : LlmGateway(
        EmptyCredentials(),
        Options.Create(new ClaudeCodeOptions()),
        Options.Create(new AnthropicOptions()),
        TimeProvider.System)
{
    public override ISummarizer? Summarizer => summarizer;

    public override ITitleTranslator? TitleTranslator => titleTranslator;

    public override ITopicClassifier? Classifier => classifier;

    public override ITopicDescriber? Describer => describer;

    public override ITopicMergeAdvisor? MergeAdvisor => mergeAdvisor;

    public override IDigestComposer? DigestComposer => digestComposer;

    public override bool IsConfigured =>
        summarizer is not null || titleTranslator is not null
        || classifier is not null || digestComposer is not null;

    static ApiCredentials EmptyCredentials() => new(
        new InMemorySecretStore(TimeProvider.System),
        new EphemeralDataProtectionProvider(),
        NullLogger<ApiCredentials>.Instance);
}
