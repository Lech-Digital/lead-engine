using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "leads",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MatchedSignals",
                table: "leads",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "Score",
                table: "leads",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "leads",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "leads",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_leads_Fingerprint",
                table: "leads",
                column: "Fingerprint",
                unique: true,
                filter: "\"Fingerprint\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_leads_Fingerprint",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "MatchedSignals",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "leads");
        }
    }
}
