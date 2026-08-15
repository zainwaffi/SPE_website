using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SPE_website.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenWaterEmailLoginProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStudentChapterOfficer",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OpenWaterMemberId",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenWaterOrganization",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenWaterProfileJson",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStudentChapterOfficer",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OpenWaterMemberId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OpenWaterOrganization",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OpenWaterProfileJson",
                table: "AspNetUsers");
        }
    }
}
