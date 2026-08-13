using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Chiezo;

/// <summary>
/// Chiezo の相手 1 つを <see cref="ICliBridge"/> として見せる。
///
/// **要約・翻訳・分類・ダイジェストの実装は変えずに済む。** どれも「システムプロンプトと
/// 本文を渡して本文を受け取る」だけなので、相手が CLI ブリッジでも Chiezo 越しの
/// Gemini でも同じ口で足りる。
/// </summary>
/// <param name="Backend">Chiezo 側の相手の識別子(`gemini` など)。</param>
/// <param name="Label">画面に出す相手の名前。</param>
/// <param name="Model">使うモデル。空なら相手の既定に任せる。</param>
/// <param name="Effort">考える量。空なら相手の既定に任せる。</param>
public record ChiezoAiSelection(string Backend, string Label, string? Model, string? Effort)
{
    /// <summary>保存・突き合わせに使うキー。**表示名は変わりうるので使わない**。</summary>
    public string Key => $"chiezo:{Backend}";

    /// <summary>画面と生成者名に出す表記(モデルまで分かるようにする)。</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Model) ? Label : $"{Label} / {Model}";
}

/// <summary>Chiezo の相手 1 つに固定した問い合わせ口。</summary>
public class ChiezoAiBridge(ChiezoAiClient client, ChiezoAiSelection selection) : ICliBridge
{
    public string Name => selection.DisplayName;

    public Task<string> RunAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        client.CompleteAsync(
            selection.Backend, selection.Model, selection.Effort,
            systemPrompt, userPrompt, cancellationToken);
}
