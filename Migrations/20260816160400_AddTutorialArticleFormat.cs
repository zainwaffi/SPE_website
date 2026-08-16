using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SPE_website.Migrations
{
    /// <inheritdoc />
    public partial class AddTutorialArticleFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArticleContent",
                table: "Tutorials",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Format",
                table: "Tutorials",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArticleContent",
                table: "Tutorials");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "Tutorials");
        }
    }
}
