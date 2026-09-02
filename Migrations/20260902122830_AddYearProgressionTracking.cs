using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ARIS1.Migrations
{
    /// <inheritdoc />
    public partial class AddYearProgressionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GraduatedAcademicYear",
                table: "Learners",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Learners",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.CreateTable(
                name: "LearnerYearRecords",
                columns: table => new
                {
                    LearnerYearRecordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LearnerId = table.Column<int>(type: "int", nullable: false),
                    AcademicYear = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<int>(type: "int", nullable: false),
                    ClassId = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerYearRecords", x => x.LearnerYearRecordId);
                    table.ForeignKey(
                        name: "FK_LearnerYearRecords_Learners_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "Learners",
                        principalColumn: "LearnerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearnerYearRecords_SchoolClasses_ClassId",
                        column: x => x.ClassId,
                        principalTable: "SchoolClasses",
                        principalColumn: "ClassId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerYearRecords_ClassId",
                table: "LearnerYearRecords",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerYearRecords_LearnerId_AcademicYear",
                table: "LearnerYearRecords",
                columns: new[] { "LearnerId", "AcademicYear" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnerYearRecords");

            migrationBuilder.DropColumn(
                name: "GraduatedAcademicYear",
                table: "Learners");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Learners");
        }
    }
}
