using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlayerSync.FileCache.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCompAlt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FileCaches_FileCaches_CompressedAlternateId",
                table: "FileCaches");

            migrationBuilder.DropIndex(
                name: "FileCaches_CompressedAlternateId",
                table: "FileCaches");

            migrationBuilder.DropColumn(
                name: "CompressedAlternateId",
                table: "FileCaches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CompressedAlternateId",
                table: "FileCaches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "FileCaches_CompressedAlternateId",
                table: "FileCaches",
                column: "CompressedAlternateId");

            migrationBuilder.AddForeignKey(
                name: "FK_FileCaches_FileCaches_CompressedAlternateId",
                table: "FileCaches",
                column: "CompressedAlternateId",
                principalTable: "FileCaches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
