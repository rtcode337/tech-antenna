using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUpvoteCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UpvoteCount",
                table: "Articles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpvoteCount",
                table: "Articles");
        }
    }
}
