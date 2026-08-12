using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddS3StorageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "s3_bucket",
                table: "datasets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "s3_prefix",
                table: "datasets",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "s3_storage_profile_name",
                table: "datasets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "s3_bucket",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "s3_prefix",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "s3_storage_profile_name",
                table: "datasets");
        }
    }
}
