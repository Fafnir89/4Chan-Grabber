using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourChanGrabber.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImageSource",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageSource", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadQueue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadUrl = table.Column<string>(type: "TEXT", nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ImageSourceId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadQueue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadQueue_ImageSource_ImageSourceId",
                        column: x => x.ImageSourceId,
                        principalTable: "ImageSource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SavePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ImageSourceId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaData_ImageSource_ImageSourceId",
                        column: x => x.ImageSourceId,
                        principalTable: "ImageSource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_ImageSourceId",
                table: "DownloadQueue",
                column: "ImageSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_Status",
                table: "DownloadQueue",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MediaData_ImageSourceId",
                table: "MediaData",
                column: "ImageSourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadQueue");

            migrationBuilder.DropTable(
                name: "MediaData");

            migrationBuilder.DropTable(
                name: "ImageSource");
        }
    }
}
