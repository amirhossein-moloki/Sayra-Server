using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryAggregationAndCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder Salter)
        {
            Salter.CreateTable(
                name: "TelemetryAggregateRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    PcId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Granularity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    WindowStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    IsPartial = table.Column<bool>(type: "boolean", nullable: false),
                    CpuMin = table.Column<double>(type: "double precision", nullable: false),
                    CpuMax = table.Column<double>(type: "double precision", nullable: false),
                    CpuAvg = table.Column<double>(type: "double precision", nullable: false),
                    CpuP50 = table.Column<double>(type: "double precision", nullable: false),
                    CpuP95 = table.Column<double>(type: "double precision", nullable: false),
                    CpuP99 = table.Column<double>(type: "double precision", nullable: false),
                    RamMin = table.Column<double>(type: "double precision", nullable: false),
                    RamMax = table.Column<double>(type: "double precision", nullable: false),
                    RamAvg = table.Column<double>(type: "double precision", nullable: false),
                    RamP50 = table.Column<double>(type: "double precision", nullable: false),
                    RamP95 = table.Column<double>(type: "double precision", nullable: false),
                    RamP99 = table.Column<double>(type: "double precision", nullable: false),
                    UptimeDelta = table.Column<double>(type: "double precision", nullable: false),
                    LaunchesDelta = table.Column<int>(type: "integer", nullable: false),
                    CrashesDelta = table.Column<int>(type: "integer", nullable: false),
                    RestartsDelta = table.Column<int>(type: "integer", nullable: false),
                    PrimaryGameName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GameSampleCount = table.Column<int>(type: "integer", nullable: false),
                    GameCpuAvg = table.Column<double>(type: "double precision", nullable: true),
                    GameCpuMax = table.Column<double>(type: "double precision", nullable: true),
                    GameRamAvg = table.Column<double>(type: "double precision", nullable: true),
                    GameRamMax = table.Column<double>(type: "double precision", nullable: true),
                    GameDurationMax = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryAggregateRecords", x => x.Id);
                });

            Salter.CreateTable(
                name: "TelemetryAggregationCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Granularity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LastProcessedWindowEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastProcessedServerTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordsProcessed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryAggregationCheckpoints", x => x.Id);
                });

            Salter.CreateIndex(
                name: "IX_TelemetryAggregateRecords_Granularity_WindowStart",
                table: "TelemetryAggregateRecords",
                columns: new[] { "Granularity", "WindowStart" });

            Salter.CreateIndex(
                name: "IX_TelemetryAggregateRecords_OrganizationId_Granularity_WindowStart_WindowEnd",
                table: "TelemetryAggregateRecords",
                columns: new[] { "OrganizationId", "Granularity", "WindowStart", "WindowEnd" });

            Salter.CreateIndex(
                name: "IX_TelemetryAggregateRecords_SiteId_Granularity_WindowStart_WindowEnd",
                table: "TelemetryAggregateRecords",
                columns: new[] { "SiteId", "Granularity", "WindowStart", "WindowEnd" });

            Salter.CreateIndex(
                name: "IX_TelemetryAggregateRecords_WorkstationId_Granularity_WindowStart",
                table: "TelemetryAggregateRecords",
                columns: new[] { "WorkstationId", "Granularity", "WindowStart" },
                unique: true);

            Salter.CreateIndex(
                name: "IX_TelemetryAggregateRecords_WorkstationId_Granularity_WindowStart_WindowEnd",
                table: "TelemetryAggregateRecords",
                columns: new[] { "WorkstationId", "Granularity", "WindowStart", "WindowEnd" });

            Salter.CreateIndex(
                name: "IX_TelemetryAggregationCheckpoints_Granularity",
                table: "TelemetryAggregationCheckpoints",
                column: "Granularity",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder Salter)
        {
            Salter.DropTable(
                name: "TelemetryAggregateRecords");

            Salter.DropTable(
                name: "TelemetryAggregationCheckpoints");
        }
    }
}
