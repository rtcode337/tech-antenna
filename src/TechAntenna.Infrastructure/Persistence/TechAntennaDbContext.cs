using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

    public DbSet<StoredTopic> Topics => Set<StoredTopic>();

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
            // 出典記事の URL。タグと同じ text[] なので同じ変換を使い回す
            ConfigureTags(book.Property(b => b.RecommendedBy));
        });

        modelBuilder.Entity<StoredTopic>(topic =>
        {
            topic.HasKey(t => t.Tag);
            topic.Property(t => t.Tag).IsRequired();
            topic.HasIndex(t => t.CollectedAt);
            topic.HasIndex(t => t.IsSelected);
        });
    }

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
