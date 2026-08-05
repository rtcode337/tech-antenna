using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>タイトル翻訳を1バッチ分だけ実行した結果。</summary>
/// <param name="Requested">対象にした論文数。</param>
/// <param name="Translated">実際に訳題が付いた件数。</param>
/// <param name="Skipped">結果に含まれず次回へ持ち越した件数。</param>
public record TitleTranslationResult(int Requested, int Translated, int Skipped)
{
    public static readonly TitleTranslationResult Nothing = new(0, 0, 0);
}

/// <summary>
/// 訳題が未処理の論文を1バッチ分だけ訳す。
///
/// **日本語のタイトル(J-STAGE の論文)も対象に含めて、空文字で確定させる。**
/// 除外して残しておくと、毎回「未処理」として取り出され続けてしまう。
/// </summary>
public class TitleTranslationRunner(
    IEnumerable<ITitleTranslator> translators,
    IArticleStore store,
    IOptions<AnthropicOptions> options,
    ILogger<TitleTranslationRunner> logger) : JobRunner
{
    readonly ITitleTranslator? _translator = translators.FirstOrDefault();

    public override string Name => $"論文タイトルの翻訳({_translator?.Name ?? "未設定"})";

    public override bool IsConfigured => _translator is not null;

    public Task<TitleTranslationResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => TranslateBatchAsync(_translator!, cancellationToken),
            TitleTranslationResult.Nothing, cancellationToken);

    async Task<TitleTranslationResult> TranslateBatchAsync(
        ITitleTranslator translator, CancellationToken cancellationToken)
    {
        var papers = await store.GetUntranslatedPapersAsync(options.Value.BatchSize, cancellationToken);
        if (papers.Count == 0)
        {
            return TitleTranslationResult.Nothing;
        }

        var results = await translator.TranslateAsync(papers, cancellationToken);

        foreach (var result in results)
        {
            // 訳さないと決めたものは空文字で確定する(次回また取り出さないため)
            await store.UpdateTitleJaAsync(result.ArticleId, result.TitleJa ?? "", cancellationToken);
        }

        var translated = results.Count(r => r.TitleJa is not null);
        logger.LogInformation(
            "{Translator}: {Total} 件中 {Translated} 件に訳題を付けた",
            translator.Name, papers.Count, translated);

        var skipped = papers.Count - results.Count;
        if (skipped > 0)
        {
            logger.LogWarning("{Skipped} 件は結果に含まれず、次回に持ち越し", skipped);
        }

        return new TitleTranslationResult(papers.Count, translated, skipped);
    }
}
