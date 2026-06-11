using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoConnectToKnxConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoConnect",
                table: "KnxConfigurations",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoConnect",
                table: "KnxConfigurations");
        }
    }
}
