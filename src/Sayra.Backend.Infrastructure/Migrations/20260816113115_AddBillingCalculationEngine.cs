using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingCalculationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumedDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    RateSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    SubtotalCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DiscountCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AdjustmentCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FinalAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    FinalCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_results_rate_snapshots_RateSnapshotId",
                        column: x => x.RateSnapshotId,
                        principalTable: "rate_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_results_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_results_CalculatedAtUtc",
                table: "billing_results",
                column: "CalculatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_billing_results_RateSnapshotId",
                table: "billing_results",
                column: "RateSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_results_SessionId",
                table: "billing_results",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_results");
        }
    }
}
