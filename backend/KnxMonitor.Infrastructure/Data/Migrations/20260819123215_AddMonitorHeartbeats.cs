using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitorHeartbeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitorHeartbeats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    TelegramsSinceLast = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitorHeartbeats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitorHeartbeats_Timestamp",
                table: "MonitorHeartbeats",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitorHeartbeats");
        }
    }
}
