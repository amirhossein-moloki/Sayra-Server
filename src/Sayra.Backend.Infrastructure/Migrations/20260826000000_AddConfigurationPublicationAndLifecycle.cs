using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Sayra.Backend.Infrastructure.Migrations
{
    public partial class AddConfigurationPublicationAndLifecycle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuration_publications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationPackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConfigurationTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IssuedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededByPublicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsRollback = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SourceVersionNumber = table.Column<long>(type: "bigint", nullable: true),
                    FailedVersionNumber = table.Column<long>(type: "bigint", nullable: true),
                    SourcePublicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfigurationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SignatureAlgorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "RSA-SHA256"),
                    SigningKeyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowVersion = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_publications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_ConfigurationPackageId_ConfigurationTargetId",
                table: "configuration_publications",
                columns: new[] { "ConfigurationPackageId", "ConfigurationTargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_ConfigurationTargetId_Active",
                table: "configuration_publications",
                column: "ConfigurationTargetId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_ConfigurationTargetId_Status",
                table: "configuration_publications",
                columns: new[] { "ConfigurationTargetId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_IdempotencyKey",
                table: "configuration_publications",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_OrganizationId_ConfigurationTargetId",
                table: "configuration_publications",
                columns: new[] { "OrganizationId", "ConfigurationTargetId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuration_publications");
        }
    }
}
