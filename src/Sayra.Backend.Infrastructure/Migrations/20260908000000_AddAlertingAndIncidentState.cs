using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertingAndIncidentState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RuleCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PcId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    LifecycleState = table.Column<int>(type: "integer", nullable: false),
                    FirstTriggeredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FiringAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TriggerEvidence = table.Column<string>(type: "text", nullable: false),
                    RecoveryEvidence = table.Column<string>(type: "text", nullable: true),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsSuppressed = table.Column<bool>(type: "boolean", nullable: false),
                    SuppressionReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ObservationCount = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Fingerprint",
                table: "Incidents",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_FirstTriggeredAtUtc",
                table: "Incidents",
                column: "FirstTriggeredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_OrganizationId_LifecycleState",
                table: "Incidents",
                columns: new[] { "OrganizationId", "LifecycleState" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_PcId_LifecycleState",
                table: "Incidents",
                columns: new[] { "PcId", "LifecycleState" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_SiteId_LifecycleState",
                table: "Incidents",
                columns: new[] { "SiteId", "LifecycleState" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Incidents");
        }
    }
}
