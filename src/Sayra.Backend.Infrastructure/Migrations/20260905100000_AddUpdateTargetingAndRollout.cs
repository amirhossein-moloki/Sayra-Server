using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Sayra.Backend.Infrastructure.Migrations
{
    public partial class AddUpdateTargetingAndRollout : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "update_targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RolloutPercentage = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MinimumSupportedVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsMandatoryOverride = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    RowVersion = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_update_targets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_update_targets_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_update_targets_update_releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "update_releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_update_targets_GroupId",
                table: "update_targets",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_update_targets_OrganizationId_ReleaseId_TargetType_SiteId_Gr~",
                table: "update_targets",
                columns: new[] { "OrganizationId", "ReleaseId", "TargetType", "SiteId", "GroupId", "WorkstationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_update_targets_OrganizationId_TargetType_IsEnabled",
                table: "update_targets",
                columns: new[] { "OrganizationId", "TargetType", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_update_targets_ReleaseId",
                table: "update_targets",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_update_targets_SiteId",
                table: "update_targets",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_update_targets_WorkstationId",
                table: "update_targets",
                column: "WorkstationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "update_targets");
        }
    }
}
