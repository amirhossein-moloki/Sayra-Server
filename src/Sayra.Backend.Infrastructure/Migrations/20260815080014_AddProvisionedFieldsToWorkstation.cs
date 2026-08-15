using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisionedFieldsToWorkstation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Workstations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "OFFLINE",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Offline");

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisioned",
                table: "Workstations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProvisionedAt",
                table: "Workstations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Sites",
                columns: new[] { "Id", "CreatedAt", "Name", "SiteId", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("6a9254d3-1823-45a4-966a-1cc12df6992d"), new DateTime(2026, 8, 15, 8, 0, 14, 201, DateTimeKind.Utc).AddTicks(62), "Site A", "SITE-A", null },
                    { new Guid("7c180905-1a8c-4fdf-973a-4be3a30fc39c"), new DateTime(2026, 8, 15, 8, 0, 14, 201, DateTimeKind.Utc).AddTicks(158), "Site Beta", "SITE-BETA", null },
                    { new Guid("bce0cf94-4d1a-45c5-9f5b-16629dfc29f2"), new DateTime(2026, 8, 15, 8, 0, 14, 201, DateTimeKind.Utc).AddTicks(138), "Site Alpha", "SITE-ALPHA", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sites_SiteId",
                table: "Sites",
                column: "SiteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropColumn(
                name: "IsProvisioned",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "ProvisionedAt",
                table: "Workstations");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Workstations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Offline",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "OFFLINE");
        }
    }
}
