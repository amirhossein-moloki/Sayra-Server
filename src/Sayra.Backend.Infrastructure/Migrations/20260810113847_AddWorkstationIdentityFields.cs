using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkstationIdentityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workstations_IpAddress",
                table: "Workstations");

            migrationBuilder.AddColumn<string>(
                name: "ClientVersion",
                table: "Workstations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Hostname",
                table: "Workstations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDisabled",
                table: "Workstations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OsVersion",
                table: "Workstations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PcId",
                table: "Workstations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SiteId",
                table: "Workstations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_LastSeen",
                table: "Workstations",
                column: "LastSeen");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_PcId",
                table: "Workstations",
                column: "PcId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_SiteId",
                table: "Workstations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_Status",
                table: "Workstations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workstations_LastSeen",
                table: "Workstations");

            migrationBuilder.DropIndex(
                name: "IX_Workstations_PcId",
                table: "Workstations");

            migrationBuilder.DropIndex(
                name: "IX_Workstations_SiteId",
                table: "Workstations");

            migrationBuilder.DropIndex(
                name: "IX_Workstations_Status",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "ClientVersion",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "Hostname",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "IsDisabled",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "OsVersion",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "PcId",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Workstations");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_IpAddress",
                table: "Workstations",
                column: "IpAddress",
                unique: true);
        }
    }
}
