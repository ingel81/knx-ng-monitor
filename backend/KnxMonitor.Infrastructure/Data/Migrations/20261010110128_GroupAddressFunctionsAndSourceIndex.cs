using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class GroupAddressFunctionsAndSourceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Functions",
                table: "GroupAddresses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnxTelegrams_Timestamp_SourceAddress",
                table: "KnxTelegrams",
                columns: new[] { "Timestamp", "SourceAddress" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KnxTelegrams_Timestamp_SourceAddress",
                table: "KnxTelegrams");

            migrationBuilder.DropColumn(
                name: "Functions",
                table: "GroupAddresses");
        }
    }
}
