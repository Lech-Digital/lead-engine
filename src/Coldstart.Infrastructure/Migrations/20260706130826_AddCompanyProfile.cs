using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfileAddress",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileCompanyName",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileContactEmail",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileKvkNumber",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileNiche",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileVatNumber",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileWebsite",
                table: "customers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileAddress",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileCompanyName",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileContactEmail",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileKvkNumber",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileNiche",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileVatNumber",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileWebsite",
                table: "customers");
        }
    }
}
