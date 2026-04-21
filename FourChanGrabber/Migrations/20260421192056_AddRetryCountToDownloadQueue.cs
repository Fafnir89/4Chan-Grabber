using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourChanGrabber.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryCountToDownloadQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RequestTime",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestTime",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "DownloadQueue");
        }
    }
}
