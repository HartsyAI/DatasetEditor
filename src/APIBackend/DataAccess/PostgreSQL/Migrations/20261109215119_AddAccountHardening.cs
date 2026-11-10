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
            // indexes. Each newer one gets the first free "-N" suffix so the case-insensitive
            // indexes can be built; the oldest account keeps its name (audit A-4). Every candidate
            // is checked against the table as it stands, so it can't collide with an existing name
            // ("Alice-1" already taken) or one assigned earlier in this loop. Emails get a "dupN-"
            // prefix the same way.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    r record;
    candidate text;
    n integer;
BEGIN
    FOR r IN
        SELECT u.id, u.username FROM users u
        WHERE EXISTS (SELECT 1 FROM users o WHERE lower(o.username) = lower(u.username)
                      AND (o.created_at, o.id) < (u.created_at, u.id))
        ORDER BY u.created_at, u.id
    LOOP
        n := 1;
        LOOP
            candidate := left(r.username, 50 - length('-' || n)) || '-' || n;
            EXIT WHEN NOT EXISTS (SELECT 1 FROM users WHERE lower(username) = lower(candidate));
            n := n + 1;
        END LOOP;
        UPDATE users SET username = candidate WHERE id = r.id;
    END LOOP;

    FOR r IN
        SELECT u.id, u.email FROM users u
        WHERE EXISTS (SELECT 1 FROM users o WHERE lower(o.email) = lower(u.email)
                      AND (o.created_at, o.id) < (u.created_at, u.id))
        ORDER BY u.created_at, u.id
    LOOP
        n := 1;
        LOOP
            candidate := left('dup' || n || '-' || r.email, 200);
            EXIT WHEN NOT EXISTS (SELECT 1 FROM users WHERE lower(email) = lower(candidate));
            n := n + 1;
        END LOOP;
        UPDATE users SET email = candidate WHERE id = r.id;
    END LOOP;
END $$;");

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
