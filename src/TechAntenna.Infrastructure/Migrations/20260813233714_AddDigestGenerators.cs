using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDigestGenerators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeneratorKey",
                table: "Digests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "Digests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RunId",
                table: "Digests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Digests_RunId",
                table: "Digests",
                column: "RunId");

            // **既存の行を「メインが 1 本だけの回」に見せる。** 既定値のままだと
            // RunId が全行ゼロで 1 つの回に混ざり、IsPrimary も false なのでホームに出なくなる
            migrationBuilder.Sql("""
                UPDATE "Digests"
                   SET "RunId" = "Id",
                       "IsPrimary" = true,
                       "GeneratorKey" = 'default'
                 WHERE "RunId" = '00000000-0000-0000-0000-000000000000';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Digests_RunId",
                table: "Digests");

            migrationBuilder.DropColumn(
                name: "GeneratorKey",
                table: "Digests");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "Digests");

            migrationBuilder.DropColumn(
                name: "RunId",
                table: "Digests");
        }
    }
}
