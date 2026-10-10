using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Nova.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounts");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "clients");

            migrationBuilder.EnsureSchema(
                name: "transactions");

            migrationBuilder.EnsureSchema(
                name: "staff");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prev_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                schema: "clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    passport_enc = table.Column<string>(type: "text", nullable: true),
                    totp_secret_enc = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    permissions = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character(20)", fixedLength: true, maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_balance_limit", "balance >= -credit_limit");
                    table.ForeignKey(
                        name: "fk_accounts_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "clients",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    login = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    totp_secret_enc = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "staff",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cards",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pan_enc = table.Column<string>(type: "text", nullable: false),
                    pan_last4 = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    expires_at = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_cards_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    from_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    comment = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfers", x => x.id);
                    table.CheckConstraint("ck_transfers_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transfers_different_accounts", "from_account_id <> to_account_id");
                    table.ForeignKey(
                        name: "fk_transfers_accounts_from_account_id",
                        column: x => x.from_account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfers_accounts_to_account_id",
                        column: x => x.to_account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "transactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_ledger_entries_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalSchema: "transactions",
                        principalTable: "transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "staff",
                table: "roles",
                columns: new[] { "id", "code", "name", "permissions" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "owner", "Владелец банка", new[] { "bank.dashboard", "reports.read", "audit.read", "staff.manage", "rates.propose", "bank.emergency_stop", "balance.adjust", "clients.read" } },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "operator", "Операционист", new[] { "clients.read", "clients.write", "accounts.open", "cards.manage" } },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "support", "Специалист поддержки", new[] { "clients.read", "cards.block" } },
                    { new Guid("00000000-0000-0000-0000-000000000004"), "credit_officer", "Кредитный специалист", new[] { "clients.read", "clients.personal_data", "credits.approve" } },
                    { new Guid("00000000-0000-0000-0000-000000000005"), "risk_manager", "Риск-менеджер", new[] { "clients.read", "fraud.review", "accounts.freeze", "bank.emergency_stop" } },
                    { new Guid("00000000-0000-0000-0000-000000000006"), "accountant", "Бухгалтер", new[] { "reports.read", "rates.approve" } },
                    { new Guid("00000000-0000-0000-0000-000000000007"), "auditor", "Аудитор", new[] { "audit.read", "reports.read" } },
                    { new Guid("00000000-0000-0000-0000-000000000008"), "sysadmin", "Системный администратор", new[] { "staff.manage" } }
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_client_id",
                schema: "accounts",
                table: "accounts",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_number",
                schema: "accounts",
                table: "accounts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_id_created_at",
                schema: "audit",
                table: "audit_log",
                columns: new[] { "actor_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_hash",
                schema: "audit",
                table: "audit_log",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cards_account_id",
                schema: "accounts",
                table: "cards",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_phone",
                schema: "clients",
                table: "clients",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_id_created_at",
                schema: "transactions",
                table: "ledger_entries",
                columns: new[] { "account_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transfer_id",
                schema: "transactions",
                table: "ledger_entries",
                column: "transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_code",
                schema: "staff",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_login",
                schema: "staff",
                table: "staff",
                column: "login",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_role_id",
                schema: "staff",
                table: "staff",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_from_account_id_created_at",
                schema: "transactions",
                table: "transfers",
                columns: new[] { "from_account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_idempotency_key",
                schema: "transactions",
                table: "transfers",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfers_to_account_id_created_at",
                schema: "transactions",
                table: "transfers",
                columns: new[] { "to_account_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "cards",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "staff",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "transfers",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "clients",
                schema: "clients");
        }
    }
}
