namespace TechAntenna.Core.Models;

/// <summary>書籍の重複判定キー。</summary>
public static class BookKey
{
    /// <summary>
    /// ISBN-13 があればそれを、無ければ書誌詳細ページの URL を、
    /// どちらも無ければタイトルをキーにする。
    /// </summary>
    public static string For(Book book) =>
        book.Isbn13 is { Length: > 0 } isbn ? $"isbn:{isbn}"
        : book.Url is { } url ? $"url:{url}"
        : $"title:{book.Title}";
}
