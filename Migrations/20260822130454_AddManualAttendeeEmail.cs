using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SPE_website.Migrations
{
    /// <inheritdoc />
    public partial class AddManualAttendeeEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "EventRegistrations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "EventRegistrations");
        }
    }
}
