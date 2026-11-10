using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedItemTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nothing ever read or wrote these tables: items live in Parquet (audit D-7).
            migrationBuilder.DropTable(
                name: "captions");

            migrationBuilder.DropTable(
                name: "dataset_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "captions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    item_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    score = table.Column<float>(type: "real", nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
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
                    caption = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_seconds = table.Column<float>(type: "real", nullable: true),
                    embedding = table.Column<byte[]>(type: "bytea", nullable: true),
                    file_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_flagged = table.Column<bool>(type: "boolean", nullable: false),
                    item_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quality_score = table.Column<float>(type: "real", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true)
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
        }
    }
}
