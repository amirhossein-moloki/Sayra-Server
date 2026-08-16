using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingTariffEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PricingPlanId",
                table: "sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pricing_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_plans_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pricing_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RateAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    GamerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: true),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                    IsPeak = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_rules_Workstations_WorkstationId",
                        column: x => x.WorkstationId,
                        principalTable: "Workstations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pricing_rules_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pricing_rules_pricing_plans_PricingPlanId",
                        column: x => x.PricingPlanId,
                        principalTable: "pricing_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rate_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    RateAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RuleReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rate_snapshots_pricing_plans_PricingPlanId",
                        column: x => x.PricingPlanId,
                        principalTable: "pricing_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rate_snapshots_pricing_rules_PricingRuleId",
                        column: x => x.PricingRuleId,
                        principalTable: "pricing_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rate_snapshots_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_PricingPlanId",
                table: "sessions",
                column: "PricingPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_plans_SiteId_Name",
                table: "pricing_plans",
                columns: new[] { "SiteId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_PricingPlanId_Priority",
                table: "pricing_rules",
                columns: new[] { "PricingPlanId", "Priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_WorkstationId",
                table: "pricing_rules",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_ZoneId",
                table: "pricing_rules",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_snapshots_PricingPlanId",
                table: "rate_snapshots",
                column: "PricingPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_snapshots_PricingRuleId",
                table: "rate_snapshots",
                column: "PricingRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_snapshots_SessionId",
                table: "rate_snapshots",
                column: "SessionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_sessions_pricing_plans_PricingPlanId",
                table: "sessions",
                column: "PricingPlanId",
                principalTable: "pricing_plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sessions_pricing_plans_PricingPlanId",
                table: "sessions");

            migrationBuilder.DropTable(
                name: "rate_snapshots");

            migrationBuilder.DropTable(
                name: "pricing_rules");

            migrationBuilder.DropTable(
                name: "pricing_plans");

            migrationBuilder.DropIndex(
                name: "IX_sessions_PricingPlanId",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "PricingPlanId",
                table: "sessions");
        }
    }
}
