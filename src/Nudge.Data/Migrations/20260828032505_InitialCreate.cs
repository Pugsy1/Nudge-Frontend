using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nudge.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstallationId = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FileLastWriteTimeUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastScannedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TableInfoTableName = table.Column<string>(type: "TEXT", nullable: true),
                    TableInfoAuthorName = table.Column<string>(type: "TEXT", nullable: true),
                    TableInfoAuthorEmail = table.Column<string>(type: "TEXT", nullable: true),
                    TableInfoAuthorWebSite = table.Column<string>(type: "TEXT", nullable: true),
                    TableInfoReleaseDate = table.Column<string>(type: "TEXT", nullable: true),
                    TableInfoVersion = table.Column<string>(type: "TEXT", nullable: true),
                    TableInfoBlurb = table.Column<string>(type: "TEXT", nullable: true),
                    TableInfoDescription = table.Column<string>(type: "TEXT", nullable: true),
                    TableInfoRules = table.Column<string>(type: "TEXT", nullable: true),
                    FilenameTitle = table.Column<string>(type: "TEXT", nullable: true),
                    FilenameManufacturer = table.Column<string>(type: "TEXT", nullable: true),
                    FilenameYear = table.Column<int>(type: "INTEGER", nullable: true),
                    FilenameTagsJson = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayTitle = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayManufacturer = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayYear = table.Column<int>(type: "INTEGER", nullable: true),
                    Confidence = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tables", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tables_DisplayTitle",
                table: "Tables",
                column: "DisplayTitle");

            migrationBuilder.CreateIndex(
                name: "IX_Tables_InstallationId_FilePath",
                table: "Tables",
                columns: new[] { "InstallationId", "FilePath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tables");
        }
    }
}
