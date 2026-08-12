using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDigestScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Digests_GeneratedAt",
                table: "Digests");

            // 既存の行は「全体と興味トピックを1本にまとめていた頃」のサマリー。
            // どちらかに振るなら全体(Overall)—— 話題度上位が材料の主だったため。
            // 空文字のままにすると読み出しで列挙子に変換できず落ちるので、必ず既定を入れる
            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "Digests",
                type: "text",
                nullable: false,
                defaultValue: "Overall");

            migrationBuilder.CreateIndex(
                name: "IX_Digests_Scope_GeneratedAt",
                table: "Digests",
                columns: new[] { "Scope", "GeneratedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Digests_Scope_GeneratedAt",
                table: "Digests");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "Digests");

            migrationBuilder.CreateIndex(
                name: "IX_Digests_GeneratedAt",
                table: "Digests",
                column: "GeneratedAt");
        }
    }
}
