using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_lab_models",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_models", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_models_application_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "model_lab_model_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ParameterSchemaVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BaseRating = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    KFactor = table.Column<int>(type: "integer", nullable: false),
                    HomeAdvantageElo = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    ProbabilityScale = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    UsesMarginAdjustment = table.Column<bool>(type: "boolean", nullable: false),
                    PointsPerEloMargin = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    CompetitionWeight = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    ExtensionDataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_model_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_model_versions_model_lab_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "model_lab_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_model_versions_CreatedAtUtc",
                table: "model_lab_model_versions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_model_versions_ModelId_VersionNumber",
                table: "model_lab_model_versions",
                columns: new[] { "ModelId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_models_OwnerUserId_IsArchived_UpdatedAtUtc",
                table: "model_lab_models",
                columns: new[] { "OwnerUserId", "IsArchived", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_models_OwnerUserId_Name",
                table: "model_lab_models",
                columns: new[] { "OwnerUserId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_lab_model_versions");

            migrationBuilder.DropTable(
                name: "model_lab_models");
        }
    }
}
