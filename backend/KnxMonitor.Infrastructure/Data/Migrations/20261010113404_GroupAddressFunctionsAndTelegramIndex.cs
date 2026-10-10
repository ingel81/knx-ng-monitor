using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class GroupAddressFunctionsAndTelegramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KnxTelegrams_Timestamp_DestinationAddress",
                table: "KnxTelegrams");

            migrationBuilder.AddColumn<string>(
                name: "Functions",
                table: "GroupAddresses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnxTelegrams_Timestamp_DestinationAddress_SourceAddress",
                table: "KnxTelegrams",
                columns: new[] { "Timestamp", "DestinationAddress", "SourceAddress" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KnxTelegrams_Timestamp_DestinationAddress_SourceAddress",
                table: "KnxTelegrams");

            migrationBuilder.DropColumn(
                name: "Functions",
                table: "GroupAddresses");

            migrationBuilder.CreateIndex(
                name: "IX_KnxTelegrams_Timestamp_DestinationAddress",
                table: "KnxTelegrams",
                columns: new[] { "Timestamp", "DestinationAddress" });
        }
    }
}
