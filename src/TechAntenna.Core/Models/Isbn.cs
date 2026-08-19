namespace TechAntenna.Core.Models;

/// <summary>ISBN の判定と変換。</summary>
public static class Isbn
{
    /// <summary>
    /// Amazon の ASIN を ISBN-13 に直す。書籍でなければ(= ISBN-10 として成り立たなければ)null。
    ///
    /// 書籍の ASIN は ISBN-10 そのものなので、記事に貼られた Amazon のリンクから
    /// 書誌を引き当てられる。ただし ASIN には `B0…` で始まる非書籍(Kindle 専売や電子機器)も
    /// あるため、チェックディジットまで検算して書籍だけを通す。
    /// </summary>
    public static string? FromAsin(string? asin)
    {
        if (asin is not { Length: 10 } || !IsValidIsbn10(asin))
        {
            return null;
        }

        // ISBN-10 → ISBN-13: 先頭に 978 を付け、チェックディジットを計算し直す
        var body = "978" + asin[..9];
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        var check = (10 - (sum % 10)) % 10;
        return body + (char)('0' + check);
    }

    /// <summary>ISBN-10 として成り立つか(末尾の X も許す)。</summary>
    public static bool IsValidIsbn10(string value)
    {
        if (value.Length != 10)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
            {
                return false;
            }

            sum += (value[i] - '0') * (10 - i);
        }

        var last = value[9];
        var checkValue = last switch
        {
            'X' or 'x' => 10,
            _ when char.IsAsciiDigit(last) => last - '0',
            _ => -1,
        };

        return checkValue >= 0 && (sum + checkValue) % 11 == 0;
    }
}
