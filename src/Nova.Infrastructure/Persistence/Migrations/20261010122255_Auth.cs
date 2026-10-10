using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Auth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.CreateSequence(
                name: "account_number_seq",
                schema: "accounts",
                startValue: 100000000L);

            migrationBuilder.AddColumn<int>(
                name: "failed_login_count",
                schema: "staff",
                table: "staff",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                schema: "staff",
                table: "staff",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "totp_enabled",
                schema: "staff",
                table: "staff",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "totp_last_step",
                schema: "staff",
                table: "staff",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "failed_login_count",
                schema: "clients",
                table: "clients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                schema: "clients",
                table: "clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "totp_enabled",
                schema: "clients",
                table: "clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "totp_last_step",
                schema: "clients",
                table: "clients",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mfa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                schema: "auth",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_subject_type_subject_id",
                schema: "auth",
                table: "refresh_tokens",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "auth",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "auth");

            migrationBuilder.DropColumn(
                name: "failed_login_count",
                schema: "staff",
                table: "staff");

            migrationBuilder.DropColumn(
                name: "locked_until",
                schema: "staff",
                table: "staff");

            migrationBuilder.DropColumn(
                name: "totp_enabled",
                schema: "staff",
                table: "staff");

            migrationBuilder.DropColumn(
                name: "totp_last_step",
                schema: "staff",
                table: "staff");

            migrationBuilder.DropColumn(
                name: "failed_login_count",
                schema: "clients",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "locked_until",
                schema: "clients",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "totp_enabled",
                schema: "clients",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "totp_last_step",
                schema: "clients",
                table: "clients");

            migrationBuilder.DropSequence(
                name: "account_number_seq",
                schema: "accounts");
        }
    }
}
