using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TunnelOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TunnelAddress",
                table: "KnxConfigurations",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TunnelAddressFallback",
                table: "KnxConfigurations",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TunnelProtocol",
                table: "KnxConfigurations",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Udp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TunnelAddress",
                table: "KnxConfigurations");

            migrationBuilder.DropColumn(
                name: "TunnelAddressFallback",
                table: "KnxConfigurations");

            migrationBuilder.DropColumn(
                name: "TunnelProtocol",
                table: "KnxConfigurations");
        }
    }
}
