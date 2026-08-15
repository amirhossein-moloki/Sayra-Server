using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGamerAccountCredentialDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Gamers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GamerId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BirthDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrganizationEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gamers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Gamers_Organizations_OrganizationEntityId",
                        column: x => x.OrganizationEntityId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Gamers_Sites_SiteEntityId",
                        column: x => x.SiteEntityId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GamerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GamerEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "SAY"),
                    Balance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BonusBalance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamerAccounts_Gamers_GamerEntityId",
                        column: x => x.GamerEntityId,
                        principalTable: "Gamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GamerCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GamerEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PasswordSalt = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HashAlgorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPasswordChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamerCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamerCredentials_Gamers_GamerEntityId",
                        column: x => x.GamerEntityId,
                        principalTable: "Gamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GamerAccounts_AccountNumber",
                table: "GamerAccounts",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamerAccounts_GamerEntityId",
                table: "GamerAccounts",
                column: "GamerEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamerCredentials_GamerEntityId",
                table: "GamerCredentials",
                column: "GamerEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Gamers_Email",
                table: "Gamers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Gamers_GamerId",
                table: "Gamers",
                column: "GamerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Gamers_OrganizationEntityId",
                table: "Gamers",
                column: "OrganizationEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Gamers_SiteEntityId",
                table: "Gamers",
                column: "SiteEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Gamers_Username",
                table: "Gamers",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GamerAccounts");

            migrationBuilder.DropTable(
                name: "GamerCredentials");

            migrationBuilder.DropTable(
                name: "Gamers");
        }
    }
}
