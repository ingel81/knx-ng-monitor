using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEtsProjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EtsProjectId",
                table: "Projects",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_EtsProjectId",
                table: "Projects",
                column: "EtsProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_EtsProjectId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "EtsProjectId",
                table: "Projects");
        }
    }
}
