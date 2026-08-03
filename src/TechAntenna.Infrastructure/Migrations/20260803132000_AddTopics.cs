using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddTopics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Topics",
            columns: table => new
            {
                Tag = table.Column<string>(type: "text", nullable: false),
                ArticleCount = table.Column<int>(type: "integer", nullable: false),
                EventCount = table.Column<int>(type: "integer", nullable: false),
                BookCount = table.Column<int>(type: "integer", nullable: false),
                CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Topics", x => x.Tag);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Topics_CollectedAt",
            table: "Topics",
            column: "CollectedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Topics");
    }
}
