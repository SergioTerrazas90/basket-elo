using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPeriodTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentPeriodEndUtc",
                table: "billing_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentPeriodStartUtc",
                table: "billing_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumStartedAtUtc",
                table: "billing_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE billing_subscriptions
                SET "PremiumStartedAtUtc" = "CreatedAtUtc"
                WHERE "IsPremium" = TRUE AND "PremiumStartedAtUtc" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentPeriodEndUtc",
                table: "billing_subscriptions");

            migrationBuilder.DropColumn(
                name: "CurrentPeriodStartUtc",
                table: "billing_subscriptions");

            migrationBuilder.DropColumn(
                name: "PremiumStartedAtUtc",
                table: "billing_subscriptions");
        }
    }
}
