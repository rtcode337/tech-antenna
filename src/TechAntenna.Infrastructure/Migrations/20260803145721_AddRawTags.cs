using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRawTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "RawTags",
                table: "Events",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "RawTags",
                table: "Books",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "RawTags",
                table: "Articles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            // 既存行には正規化後のタグしか残っていない。元の表記は取り戻せないので、
            // 手元にある値で埋めておく(再正規化を流したときに空にならないようにするため)。
            // 以降に収集した行は収集元から受け取ったままの値が入る。
            foreach (var table in new[] { "Articles", "Events", "Books" })
            {
                migrationBuilder.Sql(
                    $@"UPDATE ""{table}"" SET ""RawTags"" = ""Tags"" WHERE cardinality(""RawTags"") = 0;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawTags",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RawTags",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "RawTags",
                table: "Articles");
        }
    }
}
