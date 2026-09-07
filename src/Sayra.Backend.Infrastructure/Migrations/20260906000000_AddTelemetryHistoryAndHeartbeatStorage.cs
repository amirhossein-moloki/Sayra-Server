using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Sayra.Backend.Infrastructure.Migrations
{
    public partial class AddTelemetryHistoryAndHeartbeatStorage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelemetryHistoryRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    PcId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConnectionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cpu = table.Column<double>(type: "double precision", nullable: false),
                    Ram = table.Column<double>(type: "double precision", nullable: false),
                    Uptime = table.Column<double>(type: "double precision", nullable: false),
                    RunningGameName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RunningGamePid = table.Column<int>(type: "integer", nullable: true),
                    RunningGameCpu = table.Column<double>(type: "double precision", nullable: true),
                    RunningGameRam = table.Column<double>(type: "double precision", nullable: true),
                    RunningGameDuration = table.Column<double>(type: "double precision", nullable: true),
                    TotalLaunches = table.Column<int>(type: "integer", nullable: false),
                    TotalCrashes = table.Column<int>(type: "integer", nullable: false),
                    TotalRestarts = table.Column<int>(type: "integer", nullable: false),
                    ClientTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ServerReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryHistoryRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeartbeatHistoryRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    PcId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConnectionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ServerReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeartbeatHistoryRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryHistoryRecords_OrganizationId_ServerReceivedAt",
                table: "TelemetryHistoryRecords",
                columns: new[] { "OrganizationId", "ServerReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryHistoryRecords_ServerReceivedAt",
                table: "TelemetryHistoryRecords",
                column: "ServerReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryHistoryRecords_SiteId_ServerReceivedAt",
                table: "TelemetryHistoryRecords",
                columns: new[] { "SiteId", "ServerReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryHistoryRecords_WorkstationId_ServerReceivedAt",
                table: "TelemetryHistoryRecords",
                columns: new[] { "WorkstationId", "ServerReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HeartbeatHistoryRecords_ServerReceivedAt",
                table: "HeartbeatHistoryRecords",
                column: "ServerReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HeartbeatHistoryRecords_WorkstationId_ServerReceivedAt",
                table: "HeartbeatHistoryRecords",
                columns: new[] { "WorkstationId", "ServerReceivedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelemetryHistoryRecords");

            migrationBuilder.DropTable(
                name: "HeartbeatHistoryRecords");
        }
    }
}
