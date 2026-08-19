using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAntenna.Infrastructure.Migrations
{
    /// <summary>
    /// 推薦の出典を「URL の text[]」から「URL と題名を持つ JSON の配列」へ変える。
    ///
    /// 既存の行は捨てずに写す。EF が生成する素の AlterColumn は text[] → jsonb を
    /// キャストできず落ちるうえ、通っても中身が失われる —— 推薦は 800 冊規模で溜まっていて、
    /// 捨てると次の「定番の収集」を回すまで画面から推薦が消える。題名は当時取っていないので
    /// null で入れ、あとの収集で埋まる(BookMerge が題名を持つ側を残す)。
    /// </summary>
    public partial class ChangeBookRecommendedByToArticles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 2 段に分ける。`ALTER ... USING` にサブクエリは書けない
            // (PostgreSQL: cannot use subquery in transform expression)ので、
            // まず素直に写せる形(URL の JSON 配列)へ変えてから、UPDATE で1件ずつ組み替える。
            // 列名は JsonSerializer が書く形(宣言どおりの PascalCase)に合わせる ——
            // 既定のオプションでは読み取りも大文字小文字を区別するため
            migrationBuilder.Sql(
                """
                ALTER TABLE "Books"
                    ALTER COLUMN "RecommendedBy" TYPE jsonb USING to_jsonb("RecommendedBy");
                """);
            migrationBuilder.Sql(
                """
                UPDATE "Books" SET "RecommendedBy" = COALESCE((
                    SELECT jsonb_agg(jsonb_build_object('Url', url, 'Title', NULL))
                      FROM jsonb_array_elements_text("RecommendedBy") AS url), '[]'::jsonb);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 戻すときは題名を捨てて URL だけにする(元の形に情報を足せないので)。
            // こちらもサブクエリを使えないため、一時列へ移してから入れ替える
            migrationBuilder.Sql(
                """
                ALTER TABLE "Books"
                    ADD COLUMN "RecommendedByUrls" text[] NOT NULL DEFAULT '{}';
                """);
            migrationBuilder.Sql(
                """
                UPDATE "Books" SET "RecommendedByUrls" = COALESCE((
                    SELECT array_agg(article->>'Url')
                      FROM jsonb_array_elements("RecommendedBy") AS article), '{}'::text[]);
                """);
            migrationBuilder.Sql("""ALTER TABLE "Books" DROP COLUMN "RecommendedBy";""");
            migrationBuilder.Sql(
                """
                ALTER TABLE "Books" RENAME COLUMN "RecommendedByUrls" TO "RecommendedBy";
                """);
            migrationBuilder.Sql(
                """
                ALTER TABLE "Books" ALTER COLUMN "RecommendedBy" DROP DEFAULT;
                """);
        }
    }
}
