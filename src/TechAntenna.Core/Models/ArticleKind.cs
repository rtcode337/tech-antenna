namespace TechAntenna.Core.Models;

/// <summary>
/// 記事の種別。**同じ「読み物」でも出所の性格が違う**ので、一覧では分けて出す。
/// 保存先とタグの扱いは共通(<see cref="Article"/>)。
/// </summary>
public enum ArticleKind
{
    /// <summary>個人・企業が書いた技術記事(Zenn・Qiita・はてなブックマーク等)。</summary>
    Article,

    /// <summary>技術ニュース(Publickey・ITmedia NEWS 等)。速報性が高く、書き手の解説ではない。</summary>
    News,

    /// <summary>論文(arXiv)。**本文も要約も取り込まない**ので、要約ジョブの対象からも外す。</summary>
    Paper,
}
