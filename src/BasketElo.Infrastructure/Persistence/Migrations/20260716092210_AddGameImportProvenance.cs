using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameImportProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParserVersion",
                table: "games",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceFetchedAtUtc",
                table: "games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRevision",
                table: "games",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSeasonKey",
                table: "games",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "games",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParserVersion",
                table: "games");

            migrationBuilder.DropColumn(
                name: "SourceFetchedAtUtc",
                table: "games");

            migrationBuilder.DropColumn(
                name: "SourceRevision",
                table: "games");

            migrationBuilder.DropColumn(
                name: "SourceSeasonKey",
                table: "games");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "games");
        }
    }
}
