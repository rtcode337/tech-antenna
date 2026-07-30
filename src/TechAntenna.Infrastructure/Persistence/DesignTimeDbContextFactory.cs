using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>
/// dotnet ef のマイグレーション生成専用。DB へは接続しないため、
/// 接続文字列はプロバイダーを決めるためのダミーでよい。
/// 実行時の接続文字列はホスト側の設定(ConnectionStrings:Default)から渡される。
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TechAntennaDbContext>
{
    public TechAntennaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TechAntennaDbContext>()
            .UseNpgsql("Host=localhost;Database=techantenna;Username=postgres;Password=postgres")
            .Options;
        return new TechAntennaDbContext(options);
    }
}
