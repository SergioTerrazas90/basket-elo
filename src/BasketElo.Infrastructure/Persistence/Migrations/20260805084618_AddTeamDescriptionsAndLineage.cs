using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamDescriptionsAndLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "teams",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PredecessorTeamId",
                table: "teams",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuccessorTeamId",
                table: "teams",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_PredecessorTeamId",
                table: "teams",
                column: "PredecessorTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_teams_SuccessorTeamId",
                table: "teams",
                column: "SuccessorTeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_teams_teams_PredecessorTeamId",
                table: "teams",
                column: "PredecessorTeamId",
                principalTable: "teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_teams_teams_SuccessorTeamId",
                table: "teams",
                column: "SuccessorTeamId",
                principalTable: "teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_teams_teams_PredecessorTeamId",
                table: "teams");

            migrationBuilder.DropForeignKey(
                name: "FK_teams_teams_SuccessorTeamId",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "IX_teams_PredecessorTeamId",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "IX_teams_SuccessorTeamId",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "PredecessorTeamId",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "SuccessorTeamId",
                table: "teams");
        }
    }
}
