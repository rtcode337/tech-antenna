using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 既定値は空文字ではなく "Article"。空だと ArticleKind に対応する値が無く、
            // この列を追加する前からある行を読んだ瞬間に落ちる
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Articles",
                type: "text",
                nullable: false,
                defaultValue: "Article");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_Kind",
                table: "Articles",
                column: "Kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Articles_Kind",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Articles");
        }
    }
}
