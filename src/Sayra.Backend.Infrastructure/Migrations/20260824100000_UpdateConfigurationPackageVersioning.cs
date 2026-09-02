using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateConfigurationPackageVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConfigurationPackages_Name_Version",
                table: "ConfigurationPackages");

            migrationBuilder.AddColumn<long>(
                name: "VersionNumber",
                table: "ConfigurationPackages",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "BaseVersionNumber",
                table: "ConfigurationPackages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadType",
                table: "ConfigurationPackages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Full");

            migrationBuilder.AddColumn<string>(
                name: "SchemaVersion",
                table: "ConfigurationPackages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "1.0");

            migrationBuilder.AddColumn<string>(
                name: "IssuedBy",
                table: "ConfigurationPackages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "ConfigurationPackages",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationPackages_Name_VersionNumber",
                table: "ConfigurationPackages",
                columns: new[] { "Name", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConfigurationPackages_Name_VersionNumber",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "BaseVersionNumber",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "PayloadType",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "IssuedBy",
                table: "ConfigurationPackages");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "ConfigurationPackages",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationPackages_Name_Version",
                table: "ConfigurationPackages",
                columns: new[] { "Name", "Version" },
                unique: true);
        }
    }
}
