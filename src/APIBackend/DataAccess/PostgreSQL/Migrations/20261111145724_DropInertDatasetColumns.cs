using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class DropInertDatasetColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_datasets_format",
                table: "datasets");

            migrationBuilder.DropIndex(
                name: "IX_datasets_modality",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "format",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "parquet_path",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "total_size_bytes",
                table: "datasets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "datasets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "metadata",
                table: "datasets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parquet_path",
                table: "datasets",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "total_size_bytes",
                table: "datasets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_datasets_format",
                table: "datasets",
                column: "format");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_modality",
                table: "datasets",
                column: "modality");
        }
    }
}
