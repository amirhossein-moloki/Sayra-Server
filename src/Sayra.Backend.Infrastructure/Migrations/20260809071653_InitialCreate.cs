using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventVersion = table.Column<int>(type: "integer", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigurationPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DigitalSignature = table.Column<byte[]>(type: "bytea", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsMandatory = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemUpdates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MetricValue = table.Column<double>(type: "double precision", nullable: false),
                    DimensionJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workstations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MacAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Offline"),
                    LastSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerificationPublicKey = table.Column<byte[]>(type: "bytea", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workstations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkstationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RatePerHour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BillingAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "SAY"),
                    SessionState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkstationSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EventId",
                table: "AuditEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SessionId",
                table: "AuditEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Timestamp",
                table: "AuditEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_WorkstationId",
                table: "AuditEvents",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationPackages_Name_Version",
                table: "ConfigurationPackages",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemUpdates_Version",
                table: "SystemUpdates",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetrics_Timestamp",
                table: "TelemetryMetrics",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetrics_WorkstationId_Timestamp",
                table: "TelemetryMetrics",
                columns: new[] { "WorkstationId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_IpAddress",
                table: "Workstations",
                column: "IpAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_MacAddress",
                table: "Workstations",
                column: "MacAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationSessions_GamerId",
                table: "WorkstationSessions",
                column: "GamerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationSessions_SessionState",
                table: "WorkstationSessions",
                column: "SessionState");

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationSessions_WorkstationId",
                table: "WorkstationSessions",
                column: "WorkstationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "ConfigurationPackages");

            migrationBuilder.DropTable(
                name: "SystemUpdates");

            migrationBuilder.DropTable(
                name: "TelemetryMetrics");

            migrationBuilder.DropTable(
                name: "Workstations");

            migrationBuilder.DropTable(
                name: "WorkstationSessions");
        }
    }
}
