using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "session_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_session_segments_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_session_segments_SessionId",
                table: "session_segments",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_session_segments_SessionId_StartedAtUtc",
                table: "session_segments",
                columns: new[] { "SessionId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_session_segments_SessionId_Type",
                table: "session_segments",
                columns: new[] { "SessionId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_segments");
        }
    }
}
