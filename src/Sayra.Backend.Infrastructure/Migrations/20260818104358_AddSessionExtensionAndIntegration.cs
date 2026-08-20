using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionExtensionAndIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GamerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "SAY"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OriginalTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_FinancialTransactions_OriginalTransac~",
                        column: x => x.OriginalTransactionId,
                        principalTable: "FinancialTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_GamerAccounts_GamerAccountId",
                        column: x => x.GamerAccountId,
                        principalTable: "GamerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_LedgerEntries_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalTable: "LedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_extensions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionExtensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extended_duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    financial_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_extensions", x => x.id);
                    table.ForeignKey(
                        name: "FK_session_extensions_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GamerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "SAY"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_FinancialTransactions_FinancialTransactionId",
                        column: x => x.FinancialTransactionId,
                        principalTable: "FinancialTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_GamerAccounts_GamerAccountId",
                        column: x => x.GamerAccountId,
                        principalTable: "GamerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_CorrelationId",
                table: "FinancialTransactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_GamerAccountId",
                table: "FinancialTransactions",
                column: "GamerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_IdempotencyKey",
                table: "FinancialTransactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_LedgerEntryId",
                table: "FinancialTransactions",
                column: "LedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_OriginalTransactionId",
                table: "FinancialTransactions",
                column: "OriginalTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_Status",
                table: "FinancialTransactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_FinancialTransactionId",
                table: "Payments",
                column: "FinancialTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GamerAccountId",
                table: "Payments",
                column: "GamerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_session_extensions_idempotency_key",
                table: "session_extensions",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_session_extensions_session_id",
                table: "session_extensions",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "session_extensions");

            migrationBuilder.DropTable(
                name: "FinancialTransactions");
        }
    }
}
