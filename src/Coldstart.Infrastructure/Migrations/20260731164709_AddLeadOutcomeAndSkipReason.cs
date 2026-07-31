using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadOutcomeAndSkipReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "leads",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OutcomeAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkipReason",
                table: "leads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "OutcomeAt",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "SkipReason",
                table: "leads");
        }
    }
}
