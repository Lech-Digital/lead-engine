using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FollowUpStage",
                table: "customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "FollowUpsOptOut",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Customers that predate this feature are already days past their harvest —
            // mark them fully nudged so the first scheduler run doesn't retro-spam them.
            migrationBuilder.Sql("""UPDATE customers SET "FollowUpStage" = 2;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FollowUpStage",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "FollowUpsOptOut",
                table: "customers");
        }
    }
}
