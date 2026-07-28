using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeBillingToCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlanRenewsAt",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanRenewsAt",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "customers");
        }
    }
}
