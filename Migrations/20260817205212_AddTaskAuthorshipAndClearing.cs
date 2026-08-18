using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SPE_website.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskAuthorshipAndClearing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedByUserId",
                table: "TaskItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClearedAt",
                table: "TaskItems",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_AssignedByUserId",
                table: "TaskItems",
                column: "AssignedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_AspNetUsers_AssignedByUserId",
                table: "TaskItems",
                column: "AssignedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_AspNetUsers_AssignedByUserId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_AssignedByUserId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ClearedAt",
                table: "TaskItems");
        }
    }
}
