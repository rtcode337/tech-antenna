using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

public class TechAntennaDbContext(DbContextOptions<TechAntennaDbContext> options)
    : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();

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

            // IReadOnlyList<string> のままでは EF が扱えないため、
            // PostgreSQL の text[] 列との間で変換する
            article.Property(a => a.Tags)
                .HasConversion(
                    tags => tags.ToArray(),
                    values => values.ToList(),
                    new ValueComparer<IReadOnlyList<string>>(
                        (a, b) => a != null && b != null && a.SequenceEqual(b),
                        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                        v => v.ToList()))
                .HasColumnType("text[]");
        });
    }
}
