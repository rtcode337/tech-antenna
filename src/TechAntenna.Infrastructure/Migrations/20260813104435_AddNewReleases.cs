using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNewReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Publisher = table.Column<string>(type: "text", nullable: true),
                    PublishedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceName = table.Column<string>(type: "text", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    RawTags = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewReleases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewReleases_PublishedOn",
                table: "NewReleases",
                column: "PublishedOn");

            migrationBuilder.CreateIndex(
                name: "IX_NewReleases_Url",
                table: "NewReleases",
                column: "Url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewReleases");
        }
    }
}
