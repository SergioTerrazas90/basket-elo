using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityReviewDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_review_decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FindingType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ResolutionAction = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AffectedTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SourceTeamId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RelatedSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RelatedSourceTeamId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_review_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_review_decisions_teams_AffectedTeamId",
                        column: x => x.AffectedTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_identity_review_decisions_teams_RelatedTeamId",
                        column: x => x.RelatedTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_review_decisions_AffectedTeamId",
                table: "identity_review_decisions",
                column: "AffectedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_review_decisions_DecisionKey",
                table: "identity_review_decisions",
                column: "DecisionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_review_decisions_RelatedTeamId",
                table: "identity_review_decisions",
                column: "RelatedTeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_review_decisions");
        }
    }
}
