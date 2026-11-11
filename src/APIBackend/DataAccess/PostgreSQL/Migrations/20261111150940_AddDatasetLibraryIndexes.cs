using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasetLibraryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_datasets_created_by_user_id",
                table: "datasets");

            migrationBuilder.DropIndex(
                name: "IX_datasets_is_public",
                table: "datasets");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_created_at_public",
                table: "datasets",
                column: "created_at",
                filter: "is_public");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_created_by_user_id_created_at",
                table: "datasets",
                columns: new[] { "created_by_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_datasets_created_at_public",
                table: "datasets");

            migrationBuilder.DropIndex(
                name: "IX_datasets_created_by_user_id_created_at",
                table: "datasets");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_created_by_user_id",
                table: "datasets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_is_public",
                table: "datasets",
                column: "is_public");
        }
    }
}
