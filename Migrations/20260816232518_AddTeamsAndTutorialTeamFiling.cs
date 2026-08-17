using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SPE_website.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsAndTutorialTeamFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryRole",
                table: "Tutorials");

            migrationBuilder.CreateTable(
                name: "MemberTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Team = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberTeams_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TutorialTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TutorialId = table.Column<int>(type: "integer", nullable: false),
                    Team = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorialTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorialTeams_Tutorials_TutorialId",
                        column: x => x.TutorialId,
                        principalTable: "Tutorials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberTeams_UserId_Team",
                table: "MemberTeams",
                columns: new[] { "UserId", "Team" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TutorialTeams_TutorialId_Team",
                table: "TutorialTeams",
                columns: new[] { "TutorialId", "Team" },
                unique: true);

            // File every pre-existing tutorial under all three teams.
            //
            // This preserves current visibility rather than widening it: the old CategoryRole
            // grouped cards under headings but never filtered anything (GetForRoleAsync was
            // written and never called), so every committee member could already see every
            // tutorial. Leaving the join table empty instead would hide all existing content
            // from everyone except Team Leaders.
            migrationBuilder.Sql("""
                INSERT INTO "TutorialTeams" ("TutorialId", "Team")
                SELECT t."Id", teams.team
                FROM "Tutorials" t
                CROSS JOIN (VALUES (0), (1), (2)) AS teams(team);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberTeams");

            migrationBuilder.DropTable(
                name: "TutorialTeams");

            migrationBuilder.AddColumn<string>(
                name: "CategoryRole",
                table: "Tutorials",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
