using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    public partial class AddConfigurationSigningMetadataAndKeyRegistry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigurationHash",
                table: "ConfigurationPackages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Signature",
                table: "ConfigurationPackages",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureAlgorithm",
                table: "ConfigurationPackages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SigningKeyId",
                table: "ConfigurationPackages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConfigurationSigningKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "RSA-SHA256"),
                    PublicKeyPem = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationSigningKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationSigningKeys_KeyId",
                table: "ConfigurationSigningKeys",
                column: "KeyId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigurationSigningKeys");

            migrationBuilder.DropColumn(
                name: "ConfigurationHash",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "Signature",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "SignatureAlgorithm",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "SigningKeyId",
                table: "ConfigurationPackages");
        }
    }
}
