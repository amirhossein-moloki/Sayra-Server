using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sayra.Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityEventsAndLoginProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserResourceAccesses",
                table: "UserResourceAccesses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SecurityEvents",
                table: "SecurityEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LoginAttempts",
                table: "LoginAttempts");

            migrationBuilder.RenameTable(
                name: "UserResourceAccesses",
                newName: "user_resource_accesses");

            migrationBuilder.RenameTable(
                name: "SecurityEvents",
                newName: "security_events");

            migrationBuilder.RenameTable(
                name: "LoginAttempts",
                newName: "login_attempts");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "user_resource_accesses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Active",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ResourceType",
                table: "user_resource_accesses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TraceId",
                table: "security_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Result",
                table: "security_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ResourceType",
                table: "security_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "security_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "security_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "security_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                table: "security_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ActorType",
                table: "security_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "security_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UsernameIdentifier",
                table: "login_attempts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "IpAddress",
                table: "login_attempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "login_attempts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "login_attempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "AttemptCount",
                table: "login_attempts",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_resource_accesses",
                table: "user_resource_accesses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_security_events",
                table: "security_events",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_login_attempts",
                table: "login_attempts",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_UserResourceAccesses_Role_Type_Resource",
                table: "user_resource_accesses",
                columns: new[] { "RoleId", "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserResourceAccesses_User_Type_Resource",
                table: "user_resource_accesses",
                columns: new[] { "UserEntityId", "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_security_events_ActorId",
                table: "security_events",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_CorrelationId",
                table: "security_events",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_CreatedAt",
                table: "security_events",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_DeviceId",
                table: "security_events",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_EventType",
                table: "security_events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_ResourceId",
                table: "security_events",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_login_attempts_CreatedAt",
                table: "login_attempts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_login_attempts_IpAddress",
                table: "login_attempts",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_login_attempts_UserId",
                table: "login_attempts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_login_attempts_UsernameIdentifier",
                table: "login_attempts",
                column: "UsernameIdentifier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_user_resource_accesses",
                table: "user_resource_accesses");

            migrationBuilder.DropIndex(
                name: "IX_UserResourceAccesses_Role_Type_Resource",
                table: "user_resource_accesses");

            migrationBuilder.DropIndex(
                name: "IX_UserResourceAccesses_User_Type_Resource",
                table: "user_resource_accesses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_security_events",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_security_events_ActorId",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_security_events_CorrelationId",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_security_events_CreatedAt",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_security_events_DeviceId",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_security_events_EventType",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_security_events_ResourceId",
                table: "security_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_login_attempts",
                table: "login_attempts");

            migrationBuilder.DropIndex(
                name: "IX_login_attempts_CreatedAt",
                table: "login_attempts");

            migrationBuilder.DropIndex(
                name: "IX_login_attempts_IpAddress",
                table: "login_attempts");

            migrationBuilder.DropIndex(
                name: "IX_login_attempts_UserId",
                table: "login_attempts");

            migrationBuilder.DropIndex(
                name: "IX_login_attempts_UsernameIdentifier",
                table: "login_attempts");

            migrationBuilder.RenameTable(
                name: "user_resource_accesses",
                newName: "UserResourceAccesses");

            migrationBuilder.RenameTable(
                name: "security_events",
                newName: "SecurityEvents");

            migrationBuilder.RenameTable(
                name: "login_attempts",
                newName: "LoginAttempts");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "UserResourceAccesses",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Active");

            migrationBuilder.AlterColumn<string>(
                name: "ResourceType",
                table: "UserResourceAccesses",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "TraceId",
                table: "SecurityEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Result",
                table: "SecurityEvents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "ResourceType",
                table: "SecurityEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "SecurityEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "SecurityEvents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "SecurityEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                table: "SecurityEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ActorType",
                table: "SecurityEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "SecurityEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UsernameIdentifier",
                table: "LoginAttempts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "IpAddress",
                table: "LoginAttempts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "LoginAttempts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "LoginAttempts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "AttemptCount",
                table: "LoginAttempts",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserResourceAccesses",
                table: "UserResourceAccesses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SecurityEvents",
                table: "SecurityEvents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LoginAttempts",
                table: "LoginAttempts",
                column: "Id");
        }
    }
}
