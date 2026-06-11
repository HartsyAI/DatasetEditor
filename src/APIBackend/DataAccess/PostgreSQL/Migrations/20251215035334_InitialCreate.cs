using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    preferences = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "datasets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    modality = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    total_items = table.Column<long>(type: "bigint", nullable: false),
                    total_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    source_uri = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_streaming = table.Column<bool>(type: "boolean", nullable: false),
                    huggingface_repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    huggingface_config = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    huggingface_split = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    parquet_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_datasets", x => x.id);
                    table.ForeignKey(
                        name: "FK_datasets_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "captions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    score = table.Column<float>(type: "real", nullable: true),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_captions", x => x.id);
                    table.ForeignKey(
                        name: "FK_captions_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_captions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "dataset_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    duration_seconds = table.Column<float>(type: "real", nullable: true),
                    caption = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: true),
                    quality_score = table.Column<float>(type: "real", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    embedding = table.Column<byte[]>(type: "bytea", nullable: true),
                    is_flagged = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dataset_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_dataset_items_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    can_share = table.Column<bool>(type: "boolean", nullable: false),
                    can_delete = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    granted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_permissions_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_permissions_users_granted_by_user_id",
                        column: x => x.granted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_permissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "id", "avatar_url", "created_at", "display_name", "email", "email_verified", "is_active", "last_login_at", "password_hash", "preferences", "role", "updated_at", "username" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), null, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Administrator", "admin@localhost", true, true, null, "$2a$11$placeholder_hash_replace_on_first_run", null, "Admin", null, "admin" });

            migrationBuilder.CreateIndex(
                name: "IX_captions_created_at",
                table: "captions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_captions_created_by_user_id",
                table: "captions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_captions_dataset_id",
                table: "captions",
                column: "dataset_id");

            migrationBuilder.CreateIndex(
                name: "IX_captions_dataset_id_item_id",
                table: "captions",
                columns: new[] { "dataset_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_captions_is_primary",
                table: "captions",
                column: "is_primary");

            migrationBuilder.CreateIndex(
                name: "IX_captions_score",
                table: "captions",
                column: "score");

            migrationBuilder.CreateIndex(
                name: "IX_captions_source",
                table: "captions",
                column: "source");

            migrationBuilder.CreateIndex(
                name: "IX_dataset_items_created_at",
                table: "dataset_items",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_dataset_items_dataset_id",
                table: "dataset_items",
                column: "dataset_id");

            migrationBuilder.CreateIndex(
                name: "IX_dataset_items_dataset_id_item_id",
                table: "dataset_items",
                columns: new[] { "dataset_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dataset_items_is_deleted",
                table: "dataset_items",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_dataset_items_is_flagged",
                table: "dataset_items",
                column: "is_flagged");

            migrationBuilder.CreateIndex(
                name: "IX_dataset_items_quality_score",
                table: "dataset_items",
                column: "quality_score");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_created_at",
                table: "datasets",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_created_by_user_id",
                table: "datasets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_format",
                table: "datasets",
                column: "format");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_is_public",
                table: "datasets",
                column: "is_public");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_modality",
                table: "datasets",
                column: "modality");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_name",
                table: "datasets",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_access_level",
                table: "permissions",
                column: "access_level");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_dataset_id",
                table: "permissions",
                column: "dataset_id");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_dataset_id_user_id",
                table: "permissions",
                columns: new[] { "dataset_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permissions_expires_at",
                table: "permissions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_granted_by_user_id",
                table: "permissions",
                column: "granted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_user_id",
                table: "permissions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_created_at",
                table: "users",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_is_active",
                table: "users",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_users_role",
                table: "users",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "captions");

            migrationBuilder.DropTable(
                name: "dataset_items");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "datasets");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
