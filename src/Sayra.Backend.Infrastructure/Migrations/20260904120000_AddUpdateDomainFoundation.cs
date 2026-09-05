using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Sayra.Backend.Infrastructure.Migrations
{
    public partial class AddUpdateDomainFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "update_releases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReleaseType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReleaseNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    RowVersion = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_update_releases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_update_releases_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "update_packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    SHA256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Signature = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SigningKeyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StorageProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "local"),
                    StorageKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PackageType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RowVersion = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_update_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_update_packages_update_releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "update_releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_update_packages_LifecycleState",
                table: "update_packages",
                column: "LifecycleState");

            migrationBuilder.CreateIndex(
                name: "IX_update_packages_ReleaseId",
                table: "update_packages",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_update_packages_SHA256",
                table: "update_packages",
                column: "SHA256");

            migrationBuilder.CreateIndex(
                name: "IX_update_packages_StorageKey",
                table: "update_packages",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_update_releases_CreatedAt",
                table: "update_releases",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_update_releases_OrganizationId_Status",
                table: "update_releases",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_update_releases_OrganizationId_Version",
                table: "update_releases",
                columns: new[] { "OrganizationId", "Version" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "update_packages");

            migrationBuilder.DropTable(
                name: "update_releases");
        }
    }
}
