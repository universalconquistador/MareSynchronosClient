using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlayerSync.FileCache.Migrations
{
    /// <inheritdoc />
    public partial class InitialFileCacheDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileCaches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LastAccessedDateTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    LastModifiedDateTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    FilePathWithinFileStore = table.Column<string>(type: "TEXT", nullable: false),
                    Store = table.Column<int>(type: "INTEGER", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: -1L),
                    CompressedSize = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: -1L),
                    CompressedAlternateId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileCaches_FileCaches_CompressedAlternateId",
                        column: x => x.CompressedAlternateId,
                        principalTable: "FileCaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "FileCaches_CompressedAlternateId",
                table: "FileCaches",
                column: "CompressedAlternateId");

            migrationBuilder.CreateIndex(
                name: "FileCaches_Hash_Store",
                table: "FileCaches",
                columns: new[] { "Hash", "Store" });

            migrationBuilder.CreateIndex(
                name: "FileCaches_Store_FilePathWithinFileStore",
                table: "FileCaches",
                columns: new[] { "Store", "FilePathWithinFileStore" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "FileCaches_Store_LastAccessedDateTicks",
                table: "FileCaches",
                columns: new[] { "Store", "LastAccessedDateTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileCaches");
        }
    }
}
