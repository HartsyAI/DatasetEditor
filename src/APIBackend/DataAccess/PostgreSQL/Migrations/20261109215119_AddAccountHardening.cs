using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_email",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_username",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "security_stamp",
                value: "");

            // Every existing account gets a stamp. Cookies issued before this migration carry
            // none, so they stop matching and those users sign in once more (audit A-3).
            migrationBuilder.Sql("UPDATE users SET security_stamp = upper(md5(random()::text || id::text));");

            // Accounts that differ only in case could register under the old case-sensitive
            // indexes. The newer of each pair gets a suffix so the case-insensitive indexes can be
            // built; the oldest account keeps its name (audit A-4).
            migrationBuilder.Sql(@"
UPDATE users u SET username = left(u.username, 41) || '-' || left(u.id::text, 8)
WHERE EXISTS (SELECT 1 FROM users o WHERE lower(o.username) = lower(u.username)
              AND (o.created_at, o.id) < (u.created_at, u.id));
UPDATE users u SET email = left(u.id::text, 8) || '-' || left(u.email, 191)
WHERE EXISTS (SELECT 1 FROM users o WHERE lower(o.email) = lower(u.email)
              AND (o.created_at, o.id) < (u.created_at, u.id));");

            // EF can't model expression indexes, so these live here rather than in the model.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_users_username_lower ON users (lower(username));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_users_email_lower ON users (lower(email));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_users_email_lower;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_users_username_lower;");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);
        }
    }
}
