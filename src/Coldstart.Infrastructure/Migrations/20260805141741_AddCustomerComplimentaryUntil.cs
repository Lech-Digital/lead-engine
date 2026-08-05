using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coldstart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerComplimentaryUntil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ComplimentaryUntil",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplimentaryUntil",
                table: "customers");
        }
    }
}
