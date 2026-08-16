using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SPE_website.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Attended",
                table: "EventRegistrations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "EventRegistrations",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attended",
                table: "EventRegistrations");

            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "EventRegistrations");
        }
    }
}
