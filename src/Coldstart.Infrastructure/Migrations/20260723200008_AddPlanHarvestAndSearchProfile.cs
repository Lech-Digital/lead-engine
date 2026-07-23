using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanHarvestAndSearchProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HarvestStatus",
                table: "customers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHarvestAt",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "customers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Free");

            migrationBuilder.AddColumn<string>(
                name: "ProfileBuyingSignals",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileDisqualifiers",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileOffer",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileTargetKeywords",
                table: "customers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HarvestStatus",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "LastHarvestAt",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileBuyingSignals",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileDisqualifiers",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileOffer",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileTargetKeywords",
                table: "customers");
        }
    }
}
