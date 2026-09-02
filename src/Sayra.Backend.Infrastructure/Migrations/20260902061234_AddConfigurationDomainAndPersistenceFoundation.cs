using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationDomainAndPersistenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ConfigurationPackages",
                table: "ConfigurationPackages");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ConfigurationPackages");

            migrationBuilder.RenameTable(
                name: "ConfigurationPackages",
                newName: "configuration_packages");

            migrationBuilder.RenameIndex(
                name: "IX_ConfigurationPackages_Name_Version",
                table: "configuration_packages",
                newName: "IX_configuration_packages_Name_Version");

            migrationBuilder.AlterColumn<string>(
                name: "Version",
                table: "configuration_packages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "configuration_packages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "BaseVersion",
                table: "configuration_packages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "configuration_packages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "configuration_packages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PackageId",
                table: "configuration_packages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PayloadType",
                table: "configuration_packages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "configuration_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishedBy",
                table: "configuration_packages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Signature",
                table: "configuration_packages",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignedAt",
                table: "configuration_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerIdentity",
                table: "configuration_packages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "configuration_packages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_configuration_packages",
                table: "configuration_packages",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "CommunicationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PcId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MissedHeartbeats = table.Column<int>(type: "integer", nullable: false),
                    HeartbeatStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthenticatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisconnectReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RemoteIpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Hostname = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunicationSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "configuration_targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SiteEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkstationEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_targets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_targets_Sites_SiteEntityId",
                        column: x => x.SiteEntityId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_configuration_targets_Workstations_WorkstationEntityId",
                        column: x => x.WorkstationEntityId,
                        principalTable: "Workstations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "remote_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CommandType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetWorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetPcId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetConnectionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TargetSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    ResultPayload = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsIdempotent = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_commands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "configuration_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationPackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssignedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_assignments_configuration_packages_Configurat~",
                        column: x => x.ConfigurationPackageId,
                        principalTable: "configuration_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_configuration_assignments_configuration_targets_Configurati~",
                        column: x => x.ConfigurationTargetId,
                        principalTable: "configuration_targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configuration_publications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationPackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationTargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_publications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_publications_configuration_packages_Configura~",
                        column: x => x.ConfigurationPackageId,
                        principalTable: "configuration_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_configuration_publications_configuration_targets_Configurat~",
                        column: x => x.ConfigurationTargetId,
                        principalTable: "configuration_targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_packages_CreatedAt",
                table: "configuration_packages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_configuration_packages_PackageId_Version",
                table: "configuration_packages",
                columns: new[] { "PackageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_packages_PayloadType",
                table: "configuration_packages",
                column: "PayloadType");

            migrationBuilder.CreateIndex(
                name: "IX_configuration_packages_Status",
                table: "configuration_packages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationSessions_ConnectionId",
                table: "CommunicationSessions",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationSessions_PcId",
                table: "CommunicationSessions",
                column: "PcId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationSessions_State",
                table: "CommunicationSessions",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationSessions_WorkstationId",
                table: "CommunicationSessions",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_configuration_assignments_Package_Target_IsActive",
                table: "configuration_assignments",
                columns: new[] { "ConfigurationPackageId", "ConfigurationTargetId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_assignments_Target_IsActive_Priority",
                table: "configuration_assignments",
                columns: new[] { "ConfigurationTargetId", "IsActive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_Package_Status",
                table: "configuration_publications",
                columns: new[] { "ConfigurationPackageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_Target_Status",
                table: "configuration_publications",
                columns: new[] { "ConfigurationTargetId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_targets_SiteEntityId",
                table: "configuration_targets",
                column: "SiteEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_configuration_targets_TargetType_TargetIdentifier",
                table: "configuration_targets",
                columns: new[] { "TargetType", "TargetIdentifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_targets_WorkstationEntityId",
                table: "configuration_targets",
                column: "WorkstationEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_remote_commands_CommandId",
                table: "remote_commands",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_remote_commands_TargetPcId",
                table: "remote_commands",
                column: "TargetPcId");

            migrationBuilder.CreateIndex(
                name: "IX_remote_commands_TargetWorkstationId_Status_CreatedAt",
                table: "remote_commands",
                columns: new[] { "TargetWorkstationId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunicationSessions");

            migrationBuilder.DropTable(
                name: "configuration_assignments");

            migrationBuilder.DropTable(
                name: "configuration_publications");

            migrationBuilder.DropTable(
                name: "remote_commands");

            migrationBuilder.DropTable(
                name: "configuration_targets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_configuration_packages",
                table: "configuration_packages");

            migrationBuilder.DropIndex(
                name: "IX_configuration_packages_CreatedAt",
                table: "configuration_packages");

            migrationBuilder.DropIndex(
                name: "IX_configuration_packages_PackageId_Version",
                table: "configuration_packages");

            migrationBuilder.DropIndex(
                name: "IX_configuration_packages_PayloadType",
                table: "configuration_packages");

            migrationBuilder.DropIndex(
                name: "IX_configuration_packages_Status",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "BaseVersion",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "PayloadType",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "PublishedBy",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "Signature",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "SignedAt",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "SignerIdentity",
                table: "configuration_packages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "configuration_packages");

            migrationBuilder.RenameTable(
                name: "configuration_packages",
                newName: "ConfigurationPackages");

            migrationBuilder.RenameIndex(
                name: "IX_configuration_packages_Name_Version",
                table: "ConfigurationPackages",
                newName: "IX_ConfigurationPackages_Name_Version");

            migrationBuilder.AlterColumn<string>(
                name: "Version",
                table: "ConfigurationPackages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ConfigurationPackages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ConfigurationPackages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ConfigurationPackages",
                table: "ConfigurationPackages",
                column: "Id");
        }
    }
}
