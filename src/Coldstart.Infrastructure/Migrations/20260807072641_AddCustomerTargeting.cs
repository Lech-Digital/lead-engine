using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerTargeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetingBuyerRoles",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetingExclusions",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetingGeographies",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetingIndustries",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetingSizeMax",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetingSizeMin",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetingTriggerEvents",
                table: "customers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetingBuyerRoles",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "TargetingExclusions",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "TargetingGeographies",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "TargetingIndustries",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "TargetingSizeMax",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "TargetingSizeMin",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "TargetingTriggerEvents",
                table: "customers");
        }
    }
}
