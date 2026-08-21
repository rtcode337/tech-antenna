using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations
{
    /// <summary>
    /// 「トピックの記事で引用された」出典を持つ列を足す。推薦(RecommendedBy)とは
    /// 別の列にしてある —— 母集団が違うので、混ぜると後から分けられない。
    /// </summary>
    public partial class AddBookCitedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 既定値は EF が生成する空文字ではなく空の JSON 配列。空文字は jsonb として
            // 不正で(invalid input syntax for type json)、既存行のある DB では
            // マイグレーションそのものが落ちる
            migrationBuilder.AddColumn<string>(
                name: "CitedBy",
                table: "Books",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CitedBy",
                table: "Books");
        }
    }
}
