using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeadFingerprintPerTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_leads_CustomerId",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_Fingerprint",
                table: "leads");

            migrationBuilder.CreateIndex(
                name: "IX_leads_CustomerId_Fingerprint",
                table: "leads",
                columns: new[] { "CustomerId", "Fingerprint" },
                unique: true,
                filter: "\"Fingerprint\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_leads_CustomerId_Fingerprint",
                table: "leads");

            migrationBuilder.CreateIndex(
                name: "IX_leads_CustomerId",
                table: "leads",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_leads_Fingerprint",
                table: "leads",
                column: "Fingerprint",
                unique: true,
                filter: "\"Fingerprint\" <> ''");
        }
    }
}
