using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSiteZoneHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sites_SiteId",
                table: "Sites");

            migrationBuilder.DeleteData(
                table: "Sites",
                keyColumn: "Id",
                keyValue: new Guid("6a9254d3-1823-45a4-966a-1cc12df6992d"));

            migrationBuilder.DeleteData(
                table: "Sites",
                keyColumn: "Id",
                keyValue: new Guid("7c180905-1a8c-4fdf-973a-4be3a30fc39c"));

            migrationBuilder.DeleteData(
                table: "Sites",
                keyColumn: "Id",
                keyValue: new Guid("bce0cf94-4d1a-45c5-9f5b-16629dfc29f2"));

            migrationBuilder.AddColumn<bool>(
                name: "IsDeactivated",
                table: "Workstations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationEntityId",
                table: "Workstations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteEntityId",
                table: "Workstations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ZoneEntityId",
                table: "Workstations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Sites",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "Sites",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Sites",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "Sites",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Zones_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_OrganizationEntityId",
                table: "Workstations",
                column: "OrganizationEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_SiteEntityId",
                table: "Workstations",
                column: "SiteEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_ZoneEntityId",
                table: "Workstations",
                column: "ZoneEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_OrganizationId_Code",
                table: "Sites",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Code",
                table: "Organizations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Zones_SiteId_Code",
                table: "Zones",
                columns: new[] { "SiteId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sites_Organizations_OrganizationId",
                table: "Sites",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Workstations_Organizations_OrganizationEntityId",
                table: "Workstations",
                column: "OrganizationEntityId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Workstations_Sites_SiteEntityId",
                table: "Workstations",
                column: "SiteEntityId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Workstations_Zones_ZoneEntityId",
                table: "Workstations",
                column: "ZoneEntityId",
                principalTable: "Zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sites_Organizations_OrganizationId",
                table: "Sites");

            migrationBuilder.DropForeignKey(
                name: "FK_Workstations_Organizations_OrganizationEntityId",
                table: "Workstations");

            migrationBuilder.DropForeignKey(
                name: "FK_Workstations_Sites_SiteEntityId",
                table: "Workstations");

            migrationBuilder.DropForeignKey(
                name: "FK_Workstations_Zones_ZoneEntityId",
                table: "Workstations");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "Zones");

            migrationBuilder.DropIndex(
                name: "IX_Workstations_OrganizationEntityId",
                table: "Workstations");

            migrationBuilder.DropIndex(
                name: "IX_Workstations_SiteEntityId",
                table: "Workstations");

            migrationBuilder.DropIndex(
                name: "IX_Workstations_ZoneEntityId",
                table: "Workstations");

            migrationBuilder.DropIndex(
                name: "IX_Sites_OrganizationId_Code",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "IsDeactivated",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "OrganizationEntityId",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "SiteEntityId",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "ZoneEntityId",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "Sites");

            migrationBuilder.InsertData(
                table: "Sites",
                columns: new[] { "Id", "CreatedAt", "Name", "SiteId", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("6a9254d3-1823-45a4-966a-1cc12df6992d"), new DateTime(2026, 8, 15, 7, 30, 57, 28, DateTimeKind.Utc).AddTicks(8508), "Site A", "SITE-A", null },
                    { new Guid("7c180905-1a8c-4fdf-973a-4be3a30fc39c"), new DateTime(2026, 8, 15, 7, 30, 57, 28, DateTimeKind.Utc).AddTicks(8618), "Site Beta", "SITE-BETA", null },
                    { new Guid("bce0cf94-4d1a-45c5-9f5b-16629dfc29f2"), new DateTime(2026, 8, 15, 7, 30, 57, 28, DateTimeKind.Utc).AddTicks(8597), "Site Alpha", "SITE-ALPHA", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sites_SiteId",
                table: "Sites",
                column: "SiteId",
                unique: true);
        }
    }
}
