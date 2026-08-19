namespace TechAntenna.Core.Models;

/// <summary>
/// 記事の種別。同じ「読み物」でも出所の性格が違うので、一覧では分けて出す。
/// 保存先とタグの扱いは共通(<see cref="Article"/>)。
/// </summary>
public enum ArticleKind
{
    /// <summary>個人・企業が書いた技術記事(Zenn・Qiita・はてなブックマーク等)。</summary>
    Article,

    /// <summary>技術ニュース(Publickey・ITmedia NEWS 等)。速報性が高く、書き手の解説ではない。</summary>
    News,

    /// <summary>
    /// 論文(arXiv・J-STAGE)。収集対象に選んだトピックを検索語にして探した論文。
    /// 本文も要約も取り込まないので、要約ジョブの対象からも外す。
    /// </summary>
    Paper,

    /// <summary>
    /// いま話題の論文(Hugging Face Daily Papers)。トピックの選択に依存せず、
    /// 外の反応(投稿と upvote)で選ばれたものを拾う。
    ///
    /// <see cref="Paper"/> と分けているのは<b>探し方の軸が違う</b>から ——
    /// こちらは「外で何が話題か」、あちらは「自分の興味を深掘る」。
    /// 画面も別のセクション(直近動向 / 興味トピック)に置く。
    /// </summary>
    TrendingPaper,
}

/// <summary>種別についての小さな判定。</summary>
public static class ArticleKindExtensions
{
    /// <summary>
    /// 論文か(トピック検索由来・話題由来のどちらも)。本文を取り込まないので要約の対象外、
    /// 一方でタイトルは英語のことが多いので翻訳の対象になる。
    /// </summary>
    public static bool IsPaper(this ArticleKind kind) =>
        kind is ArticleKind.Paper or ArticleKind.TrendingPaper;
}
