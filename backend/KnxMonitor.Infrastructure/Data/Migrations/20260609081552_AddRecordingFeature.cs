using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordingFeature : Migration
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

            migrationBuilder.CreateTable(
                name: "RecordingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HotBufferMaxCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ArchiveEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ArchiveRetentionDays = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordingSettings");

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
