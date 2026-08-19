namespace TechAntenna.Web.Services;

/// <summary>ジョブの実行結果を画面に出す文言にする。</summary>
public static class JobMessage
{
    public static string Describe(CollectionRunResult result)
    {
        // 何も集まらなかった理由が分かっているならそれを出す(推測混じりの定型文より役に立つ)
        if (result.Note is { Length: > 0 } note)
        {
            return note;
        }

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

    /// <summary>話題度を取り直した結果。</summary>
    public static string Describe(TrendRefreshResult result) =>
        $"話題度を取り直しました：{result.Count} 件のトピックを更新（うち {result.Trending} 件に話題度が付きました）。"
        + (result.FailedSources > 0 ? $" {result.FailedSources} 件の収集元が失敗しています。" : "");

    /// <summary>
    /// タグを仕分けなおした結果。トピックとタグの両方の画面から押せるので、
    /// 文言はここに1つ置く(画面ごとに書くとずれる)。
    /// </summary>
    public static string Describe(TagClassificationResult result)
    {
        if (result.Asked == 0 && result.Merged == 0 && result.Described == 0)
        {
            // 押しても何も起きないのが正常な状態(仕分け待ちが尽きた)なので、そう書く
            return $"仕分け待ちのタグはありませんでした（{result.Count} 件のトピックを更新）。"
                + "新しい語は収集と「話題度を取り直す」で増えます。";
        }

        return $"LLM に {result.Asked} 件のタグを聞き、{result.Classified} 件を語彙に入れました"
            + $"（{result.Count} 件のトピックを更新）。"
            + (result.Merged > 0 ? $" 同義の {result.Merged} 件を寄せました。" : "")
            + (result.Described > 0 ? $" {result.Described} 件の用語に説明を付けました。" : "");
    }

    /// <summary>
    /// ファイル取り込みの結果。ジョブではない(その場で終わる)が、文言の作り方は
    /// ほかの結果とそろえたいのでここに置く。
    /// </summary>
    public static string Describe(TopicImportResult result)
    {
        var message = $"語彙 {result.Topics} 件（新しく増えたのは {result.TopicsAdded} 件）、"
            + $"タグの仕分け {result.Tags} 件（初めて見た語は {result.TagsAdded} 件）を取り込みました。";

        if (result.Selected > 0)
        {
            message += $" 収集対象を {result.Selected} 件に置き換えました。";
        }

        if (result.KeptHuman > 0)
        {
            message += $" {result.KeptHuman} 件はこの環境で人が直した仕分けなので、そのまま残しました。";
        }

        if (result.DroppedParents > 0)
        {
            message += $" {result.DroppedParents} 件は親が見つからないので最上位にしました。";
        }

        if (result.DroppedAliases > 0)
        {
            message += $" {result.DroppedAliases} 件は寄せ先が見つからないので取り込みませんでした。";
        }

        return message + " 件数と話題度は「話題度を取り直す」「タグを仕分けなおす」で集め直されます。";
    }

    /// <summary>定期実行を1回通した結果。個々のジョブの文言は各行に出るので、ここは要約だけ。</summary>
    public static string Describe(ScheduleRunResult result)
    {
        if (result == ScheduleRunResult.Nothing)
        {
            return "定期実行に入れたジョブがありません(ボタンの左のチェックを入れてください)。";
        }

        var message = $"{result.Total} 件中 {result.Ran} 件を実行しました。";
        if (result.Failed > 0)
        {
            message += $" {result.Failed} 件が失敗(各ジョブの行に理由が出ます)。";
        }

        if (result.Skipped > 0)
        {
            message += $" {result.Skipped} 件は設定が足りないので飛ばしました。";
        }

        return message;
    }

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
