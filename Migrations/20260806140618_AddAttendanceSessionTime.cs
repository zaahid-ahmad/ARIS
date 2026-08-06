using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ARIS1.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceSessionTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "Time",
                table: "AttendanceSessions",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Time",
                table: "AttendanceSessions");
        }
    }
}
