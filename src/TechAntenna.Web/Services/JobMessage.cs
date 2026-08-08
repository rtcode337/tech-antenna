namespace TechAntenna.Web.Services;

/// <summary>ジョブの実行結果を画面に出す文言にする。</summary>
public static class JobMessage
{
    public static string Describe(CollectionRunResult result)
    {
        if (result == CollectionRunResult.Nothing)
        {
            // 収集元が無いか、既に走っている最中に押された
            return "何も収集しませんでした(実行中か、収集元が未設定)。";
        }

        var message = $"{result.Fetched} 件取得、うち {result.Added} 件を新規追加しました。";
        return result.FailedSources > 0
            ? $"{message} {result.FailedSources} 件の収集元で失敗(詳細はログ)。"
            : message;
    }

    /// <summary>
    /// トピック再編成の結果。**トピックとタグの両方の画面から押せる**ので、
    /// 文言はここに1つ置く(画面ごとに書くとずれる)。
    /// </summary>
    public static string Describe(TopicReorganizationResult result) =>
        $"{result.Count} 件のトピックを更新しました（うち {result.Trending} 件に話題度が付きました）。"
        + (result.Classified > 0 ? $" LLM が {result.Classified} 件のタグを仕分けました。" : "")
        + (result.Merged > 0 ? $" 同義の {result.Merged} 件を寄せました。" : "")
        + (result.Described > 0 ? $" {result.Described} 件の用語に説明を付けました。" : "")
        + (result.FailedSources > 0 ? $" {result.FailedSources} 件の収集元が失敗しています。" : "");

    public static string Describe(SummaryRunResult result)
    {
        if (result == SummaryRunResult.Nothing)
        {
            return "要約が必要な記事はありませんでした。";
        }

        var message = $"{result.Requested} 件中 {result.Summarized} 件を要約しました。";
        return result.Skipped > 0
            ? $"{message} {result.Skipped} 件は次回に持ち越し。"
            : message;
    }
}
