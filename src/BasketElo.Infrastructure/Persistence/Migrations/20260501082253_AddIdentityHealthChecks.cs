using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityHealthChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_team_aliases_Source_SourceTeamId",
                table: "team_aliases");

            migrationBuilder.CreateTable(
                name: "identity_health_check_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Season = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RulesVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FindingsCount = table.Column<int>(type: "integer", nullable: false),
                    UnresolvedBlockersCount = table.Column<int>(type: "integer", nullable: false),
                    Forced = table.Column<bool>(type: "boolean", nullable: false),
                    CheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_health_check_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_health_check_runs_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "identity_health_check_findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    FindingType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SourceTeamId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AffectedTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RelatedSourceTeamId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RelatedTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    Season = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Evidence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SuggestedAction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ResolutionAction = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_health_check_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_health_check_findings_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_identity_health_check_findings_identity_health_check_runs_R~",
                        column: x => x.RunId,
                        principalTable: "identity_health_check_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_identity_health_check_findings_teams_AffectedTeamId",
                        column: x => x.AffectedTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_identity_health_check_findings_teams_RelatedTeamId",
                        column: x => x.RelatedTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_team_aliases_Source_SourceTeamId",
                table: "team_aliases",
                columns: new[] { "Source", "SourceTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_team_aliases_Source_SourceTeamId_AliasName",
                table: "team_aliases",
                columns: new[] { "Source", "SourceTeamId", "AliasName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_findings_AffectedTeamId",
                table: "identity_health_check_findings",
                column: "AffectedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_findings_CompetitionId",
                table: "identity_health_check_findings",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_findings_RelatedTeamId",
                table: "identity_health_check_findings",
                column: "RelatedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_findings_RunId",
                table: "identity_health_check_findings",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_findings_Season_CountryCode",
                table: "identity_health_check_findings",
                columns: new[] { "Season", "CountryCode" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_findings_Source_SourceTeamId",
                table: "identity_health_check_findings",
                columns: new[] { "Source", "SourceTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_findings_Status_Severity",
                table: "identity_health_check_findings",
                columns: new[] { "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_runs_CompetitionId",
                table: "identity_health_check_runs",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_runs_ScopeKey",
                table: "identity_health_check_runs",
                column: "ScopeKey");

            migrationBuilder.CreateIndex(
                name: "IX_identity_health_check_runs_Status_CheckedAtUtc",
                table: "identity_health_check_runs",
                columns: new[] { "Status", "CheckedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_health_check_findings");

            migrationBuilder.DropTable(
                name: "identity_health_check_runs");

            migrationBuilder.DropIndex(
                name: "IX_team_aliases_Source_SourceTeamId",
                table: "team_aliases");

            migrationBuilder.DropIndex(
                name: "IX_team_aliases_Source_SourceTeamId_AliasName",
                table: "team_aliases");

            migrationBuilder.CreateIndex(
                name: "IX_team_aliases_Source_SourceTeamId",
                table: "team_aliases",
                columns: new[] { "Source", "SourceTeamId" },
                unique: true);
        }
    }
}
