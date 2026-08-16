using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PausedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sessions_Gamers_GamerId",
                        column: x => x.GamerId,
                        principalTable: "Gamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sessions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sessions_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sessions_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sessions_Workstations_WorkstationId",
                        column: x => x.WorkstationId,
                        principalTable: "Workstations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_GamerId_Status",
                table: "sessions",
                columns: new[] { "GamerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_OrganizationId",
                table: "sessions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_ReservationId_Status",
                table: "sessions",
                columns: new[] { "ReservationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_SiteId",
                table: "sessions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_Status",
                table: "sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_WorkstationId_Status",
                table: "sessions",
                columns: new[] { "WorkstationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sessions");
        }
    }
}
