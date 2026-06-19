using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnxMonitor.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationsCommObjectsAndKeyring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommunicationObjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeviceAddress = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FunctionText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    GroupAddressLink = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DatapointType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Flags = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunicationObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunicationObjects_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ParentExternalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DeviceAddresses = table.Column<string>(type: "TEXT", nullable: true),
                    GroupAddresses = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectKeyringKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    KeyType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    GroupAddress = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    IndividualAddress = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    KeyBase64 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectKeyringKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectKeyringKeys_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationObjects_ProjectId_DeviceAddress",
                table: "CommunicationObjects",
                columns: new[] { "ProjectId", "DeviceAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationObjects_ProjectId_GroupAddressLink",
                table: "CommunicationObjects",
                columns: new[] { "ProjectId", "GroupAddressLink" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_ProjectId_ExternalId",
                table: "Locations",
                columns: new[] { "ProjectId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectKeyringKeys_ProjectId_KeyType",
                table: "ProjectKeyringKeys",
                columns: new[] { "ProjectId", "KeyType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunicationObjects");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "ProjectKeyringKeys");
        }
    }
}
