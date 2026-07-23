using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyBrief : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfileBrief",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileMentionSourceTool",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileBrief",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "ProfileMentionSourceTool",
                table: "customers");
        }
    }
}
