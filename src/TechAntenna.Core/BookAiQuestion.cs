using System.Text;
using TechAntenna.Core.Models;

namespace TechAntenna.Core;

/// <summary>
/// 「その本について AI に聞く」リンクを組む。書名の横の印を押すと、
/// その本を買える場所と、どんな本かの解説を AI が答える。
///
/// 一覧に並ぶのは書名・著者・出版社と、記事にどれだけ名指しされたかだけで、
/// <b>中身が自分に要る本かどうかは分からない</b>(出版社の著作物なので紹介文は
/// 取り込んでいない)。そこを外の AI に聞かせるためのリンク。
///
/// <b>行き先は Gemini 本体ではなく Google の AI モード</b>(<c>udm=50</c>)。
/// Gemini(<c>gemini.google.com/app</c>)は URL でプロンプトを渡す口を公式には
/// 持たず、<c>?q=</c> を読ませるには Chrome 拡張が要る —— 入れていない端末
/// (スマホを含む)では空の Gemini が開くだけになる。AI モードなら答えるのは同じ
/// Gemini で、拡張なしでどの端末でも開き、回答に出典のリンクが並ぶ。
///
/// <b>プロンプトは URL に載る</b>ので、際限なく長くしない —— 書名は
/// <see cref="MaxTitleChars"/> 文字、著者は <see cref="MaxAuthors"/> 人で切る
/// (NDL 由来の書名は副題まで入って長い)。上限が要るからではない
/// (実測では 4,400 文字の URL でも Google は 414 を返さなかった)——
/// <b>問いが長いほど答えがぼやける</b>ので、本を特定できるだけの手がかりに絞る。
/// </summary>
public static class BookAiQuestion
{
    /// <summary>Google の AI モード。<c>udm=50</c> がそのモードの印。</summary>
    const string SearchBase = "https://www.google.com/search?udm=50&q=";

    /// <summary>プロンプトに載せる書名の長さ。超えたぶんは落として `…` を付ける。</summary>
    const int MaxTitleChars = 120;

    /// <summary>プロンプトに載せる著者の数。超えたぶんは「ほか」でまとめる。</summary>
    const int MaxAuthors = 3;

    /// <summary>AI に投げる問い。</summary>
    public static string Prompt(Book book)
    {
        var question = new StringBuilder();
        question.Append('『').Append(Shorten(book.Title)).Append('』');

        // 同名・改訂版の取り違えを防ぐ手がかり(著者・出版社・ISBN)。取れているものだけ添える
        var facts = Facts(book);
        if (facts.Length > 0)
        {
            question.Append('(').Append(facts).Append(')');
        }

        // 買える場所を先に言わせる —— 押した人がまず知りたいのはそこで、
        // 解説から始めると長い前置きの後ろに埋もれる
        question.Append(
            "について、まず日本でこの本を購入できるサイトのリンクを並べてください。"
            + "そのあとで、どんな内容の本で、何が学べて、どんな人に向いているかを解説してください。");

        return question.ToString();
    }

    /// <summary>押したときに開く URL。</summary>
    public static Uri Url(Book book) =>
        new(SearchBase + Uri.EscapeDataString(Prompt(book)));

    /// <summary>書名に添える事実。無いものは飛ばす(空欄を「著者不明」とは書かない)。</summary>
    static string Facts(Book book)
    {
        var facts = new List<string>(3);

        if (book.Authors.Count > 0)
        {
            var authors = string.Join("、", book.Authors.Take(MaxAuthors));
            facts.Add(book.Authors.Count > MaxAuthors ? authors + " ほか" : authors);
        }

        if (!string.IsNullOrWhiteSpace(book.Publisher))
        {
            facts.Add(book.Publisher.Trim());
        }

        if (!string.IsNullOrWhiteSpace(book.Isbn13))
        {
            facts.Add("ISBN " + book.Isbn13);
        }

        return string.Join("、", facts);
    }

    static string Shorten(string title)
    {
        var trimmed = title.Trim();

        return trimmed.Length <= MaxTitleChars ? trimmed : trimmed[..MaxTitleChars] + "…";
    }
}
