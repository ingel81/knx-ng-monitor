using KnxMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Reduces the KnxTelegrams indices from 5 to 2 for the count-based ring buffer.
    /// Keeps the composite (Timestamp, DestinationAddress) and the GroupAddressId FK index;
    /// drops the standalone Timestamp, DestinationAddress and MessageType indices.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260608120000_ReduceKnxTelegramIndices")]
    public partial class ReduceKnxTelegramIndices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KnxTelegrams_DestinationAddress",
                table: "KnxTelegrams");

            migrationBuilder.DropIndex(
                name: "IX_KnxTelegrams_MessageType",
                table: "KnxTelegrams");

            migrationBuilder.DropIndex(
                name: "IX_KnxTelegrams_Timestamp",
                table: "KnxTelegrams");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_KnxTelegrams_DestinationAddress",
                table: "KnxTelegrams",
                column: "DestinationAddress");

            migrationBuilder.CreateIndex(
                name: "IX_KnxTelegrams_MessageType",
                table: "KnxTelegrams",
                column: "MessageType");

            migrationBuilder.CreateIndex(
                name: "IX_KnxTelegrams_Timestamp",
                table: "KnxTelegrams",
                column: "Timestamp");
        }
    }
}
