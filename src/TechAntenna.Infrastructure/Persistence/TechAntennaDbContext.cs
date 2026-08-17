using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Persistence;

public class TechAntennaDbContext(DbContextOptions<TechAntennaDbContext> options)
    : DbContext(options)
{
    /// <summary>書籍の重複判定キーを保持するシャドウプロパティの名前。</summary>
    public const string BookDedupKey = "DedupKey";

    public DbSet<Article> Articles => Set<Article>();

    public DbSet<TechEvent> Events => Set<TechEvent>();

    public DbSet<Book> Books => Set<Book>();

    /// <summary>最近出た本の観測(出版のテーマを数えるための材料)。</summary>
    public DbSet<NewRelease> NewReleases => Set<NewRelease>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<Digest> Digests => Set<Digest>();

    public DbSet<Secret> Secrets => Set<Secret>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Article>(article =>
        {
            article.HasKey(a => a.Id);

            article.Property(a => a.Title).IsRequired();

            article.Property(a => a.Url)
                .HasConversion(url => url.ToString(), value => new Uri(value))
                .IsRequired();
            // URL を重複判定のキーにする(IArticleStore の契約)
            article.HasIndex(a => a.Url).IsUnique();

            article.Property(a => a.SourceName).IsRequired();

            // 種別は数値ではなく名前で持つ(SQL で覗いたときに読めるほうを優先)
            article.Property(a => a.Kind).HasConversion<string>().IsRequired();
            article.HasIndex(a => a.Kind);

            ConfigureTags(article.Property(a => a.Tags));
            ConfigureTags(article.Property(a => a.RawTags));
        });

        // 最近出た本の観測。**書籍(Book)とは別の表**にしてある —— あちらは「読んでおくべき本」で
        // レビュー・推薦・書影を伴って一覧に並ぶもの、こちらは数えるためだけに集める観測。
        // 混ぜると書籍の一覧が新刊で埋まる(読み込みの窓を新刊が食う)
        modelBuilder.Entity<NewRelease>(release =>
        {
            release.HasKey(r => r.Id);

            release.Property(r => r.Title).IsRequired();

            release.Property(r => r.Url)
                .HasConversion(url => url.ToString(), value => new Uri(value))
                .IsRequired();
            // URL を重複判定のキーにする(INewReleaseStore の契約)
            release.HasIndex(r => r.Url).IsUnique();

            release.Property(r => r.SourceName).IsRequired();

            // 集計はいつも「直近 N か月」で切るので、刊行日に索引を張る
            release.HasIndex(r => r.PublishedOn);

            ConfigureTags(release.Property(r => r.Tags));
            ConfigureTags(release.Property(r => r.RawTags));
        });

        modelBuilder.Entity<TechEvent>(techEvent =>
        {
            techEvent.HasKey(e => e.Id);

            techEvent.Property(e => e.Title).IsRequired();

            techEvent.Property(e => e.Url)
                .HasConversion(url => url.ToString(), value => new Uri(value))
                .IsRequired();
            // URL を重複判定のキーにする(IEventStore の契約)
            techEvent.HasIndex(e => e.Url).IsUnique();

            techEvent.Property(e => e.SourceName).IsRequired();

            // 「これから開催されるイベント」の問い合わせで使う
            techEvent.HasIndex(e => e.StartsAt);

            ConfigureTags(techEvent.Property(e => e.Tags));
            ConfigureTags(techEvent.Property(e => e.RawTags));
        });

        modelBuilder.Entity<Book>(book =>
        {
            book.HasKey(b => b.Id);

            book.Property(b => b.Title).IsRequired();

            book.Property(b => b.SourceName).IsRequired();

            // ISBN・URL・タイトルのいずれかから作る重複判定キー(IBookStore の契約)。
            // ドメインモデルを永続化の都合で汚さないよう、シャドウプロパティとして持つ
            book.Property<string>(BookDedupKey).IsRequired();
            book.HasIndex(BookDedupKey).IsUnique();

            book.Property(b => b.Url)
                .HasConversion(url => url!.ToString(), value => new Uri(value));

            book.Property(b => b.CoverUrl)
                .HasConversion(url => url!.ToString(), value => new Uri(value));

            // 著者名は順序に意味があるため、タグと違って正規化せず配列のまま保存する
            book.Property(b => b.Authors)
                .HasConversion(
                    value => value.ToArray(),
                    value => value.ToList(),
                    new ValueComparer<IReadOnlyList<string>>(
                        (a, b) => a != null && b != null && a.SequenceEqual(b),
                        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                        v => v.ToList()))
                .HasColumnType("text[]");

            ConfigureTags(book.Property(b => b.Tags));
            ConfigureTags(book.Property(b => b.RawTags));
            // 出典記事は URL と題名の組なので JSON 1 列で持つ(ダイジェストの項目と同じ流儀)。
            // **かつては URL だけの text[]** だったが、画面が番号ではなく題名を出すようになり、
            // 2 つの値を1件として持つ必要が出た。出典単体で検索・集計する予定は無く、
            // 常に本を丸ごと読み書きするので、行に正規化してもテーブルが増えるだけ
            book.Property(b => b.RecommendedBy)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    value => JsonSerializer.Deserialize<List<RecommendedArticle>>(
                        value, (JsonSerializerOptions?)null) ?? new List<RecommendedArticle>(),
                    new ValueComparer<IReadOnlyList<RecommendedArticle>>(
                        (a, b) => a != null && b != null && a.SequenceEqual(b),
                        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                        v => v.ToList()))
                .HasColumnType("jsonb")
                .IsRequired();
        });

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.HasKey(t => t.Key);
            tag.Property(t => t.Key).IsRequired();
            // 状態と出どころは数値ではなく名前で持つ(SQL で覗いたときに読めるほうを優先)
            tag.Property(t => t.Status).HasConversion<string>().IsRequired();
            tag.Property(t => t.DecidedBy).HasConversion<string>().IsRequired();
            // 「次に聞く語」を引くための索引(状態 + 再挑戦の期限)
            tag.HasIndex(t => new { t.Status, t.RetryAfter });
            tag.HasIndex(t => t.TopicKey);
        });

        modelBuilder.Entity<Topic>(topic =>
        {
            topic.HasKey(t => t.Key);
            topic.Property(t => t.Key).IsRequired();
            topic.Property(t => t.Display).IsRequired();
            topic.Property(t => t.DecidedBy).HasConversion<string>().IsRequired();
            topic.HasIndex(t => t.IsSelected);
            topic.HasIndex(t => t.Parent);
        });

        modelBuilder.Entity<Digest>(digest =>
        {
            digest.HasKey(d => d.Id);

            digest.Property(d => d.Lead).IsRequired();
            digest.Property(d => d.GeneratorName).IsRequired();
            digest.Property(d => d.GeneratorKey).IsRequired();

            // 守備範囲は数値ではなく名前で持つ(記事の種別と同じ流儀 —— SQL で覗いたときに読める)
            digest.Property(d => d.Scope)
                .HasConversion<string>()
                .IsRequired();

            // 「守備範囲ごとの最新の1件」を引くための索引(ホームが範囲ごとに引くため)
            digest.HasIndex(d => new { d.Scope, d.GeneratedAt });

            // 同じ回で作った束(複数の AI で同時に作ったもの)をまとめて引くための索引
            digest.HasIndex(d => d.RunId);

            // 項目は行を分けず JSON 1 列で持つ。項目単体を検索・集計する予定が無く、
            // 常にダイジェスト丸ごとで読み書きするため(正規化してもテーブルが増えるだけ)
            digest.Property(d => d.Items)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    value => JsonSerializer.Deserialize<List<DigestItem>>(
                        value, (JsonSerializerOptions?)null) ?? new List<DigestItem>(),
                    new ValueComparer<IReadOnlyList<DigestItem>>(
                        (a, b) => a != null && b != null && a.SequenceEqual(b),
                        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                        v => v.ToList()))
                .HasColumnType("jsonb")
                .IsRequired();
        });

        modelBuilder.Entity<Secret>(secret =>
        {
            // 設定キー(例 "Connpass:ApiKey")がそのまま主キー。1 キー 1 行
            secret.HasKey(s => s.Name);

            // 値は Web 層が Data Protection で暗号化した文字列(平文は入らない)
            secret.Property(s => s.Value).IsRequired();
        });

        // **日時は DB に渡す直前に UTC へそろえる。** Npgsql は `timestamp with time zone` に
        // 時差 0 以外の DateTimeOffset を書けず、そのまま渡すと実行時に落ちる:
        //   Cannot write DateTimeOffset with Offset=09:00:00 to PostgreSQL type
        //   'timestamp with time zone', only offset 0 (UTC) is supported.
        // 収集元は時差を付けたまま返してくる(connpass の `started_at` は `+09:00`)ので、
        // **保存も問い合わせのパラメータもここを通す** —— 収集元ごとに `ToUniversalTime()` を
        // 書いて回ると、書き忘れた1つが「その収集元だけ 1 件も保存されない」になる
        // (実際、記事のパーサだけが直していて connpass / Doorkeeper のイベントは
        // 保存できていなかった。カレンダーの月の範囲を JST のまま渡して画面も落ちた)。
        // 列は timestamptz(= 時点)のままなので、時差そのものは元から保存されない。
        // 人に見せるときは JapanTime で JST に直す(CLAUDE.md「日時の表示」)。
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(ToUtc);
            }
            else if (property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(ToUtcNullable);
            }
        }
    }

    /// <summary>保存の直前に UTC へそろえる(読み出しは UTC のまま返る)。</summary>
    static readonly ValueConverter<DateTimeOffset, DateTimeOffset> ToUtc =
        new(value => value.ToUniversalTime(), value => value);

    static readonly ValueConverter<DateTimeOffset?, DateTimeOffset?> ToUtcNullable =
        new(value => value.HasValue ? value.Value.ToUniversalTime() : value, value => value);

    // IReadOnlyList<string> のままでは EF が扱えないため、PostgreSQL の text[] 列との間で変換する
    static void ConfigureTags(PropertyBuilder<IReadOnlyList<string>> tags) =>
        tags.HasConversion(
                value => value.ToArray(),
                value => value.ToList(),
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                    v => v.ToList()))
            .HasColumnType("text[]");
}
