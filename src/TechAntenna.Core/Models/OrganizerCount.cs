namespace TechAntenna.Core.Models;

/// <summary>ある主催者のイベントが何件あるか。公式の名簿(設定 → 主催者)を直すときの材料。</summary>
public record OrganizerCount(string Organizer, int Count);
