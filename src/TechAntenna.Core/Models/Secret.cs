namespace TechAntenna.Core.Models;

/// <summary>
/// 画面から設定した API キー・トークンの1件。値は保存する側(Web 層)が
/// Data Protection で暗号化してから渡すので、ここには保護済みの文字列が入る。
/// </summary>
public class Secret
{
    /// <summary>設定キー(構成のパスと同じ表記。例 "Connpass:ApiKey")。</summary>
    public required string Name { get; set; }

    /// <summary>保護済みの値。平文は DB に置かない。</summary>
    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
