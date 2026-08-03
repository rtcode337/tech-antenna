using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations;

public partial class AddTrendTopicMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "IsSelected", table: "Topics", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>(name: "SourceCount", table: "Topics", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "TrendScore", table: "Topics", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.CreateIndex(name: "IX_Topics_IsSelected", table: "Topics", column: "IsSelected");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Topics_IsSelected", table: "Topics");
        migrationBuilder.DropColumn(name: "IsSelected", table: "Topics");
        migrationBuilder.DropColumn(name: "SourceCount", table: "Topics");
        migrationBuilder.DropColumn(name: "TrendScore", table: "Topics");
    }
}
