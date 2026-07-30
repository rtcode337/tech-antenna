using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

public class TechAntennaDbContext(DbContextOptions<TechAntennaDbContext> options)
    : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();

    public DbSet<TechEvent> Events => Set<TechEvent>();

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

            ConfigureTags(article.Property(a => a.Tags));
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
