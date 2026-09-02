using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ARIS1.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndRiskSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearnerYearSubjectRisks",
                columns: table => new
                {
                    LearnerYearSubjectRiskId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LearnerId = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    AcademicYear = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AcademicAverage = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    AttendancePercentage = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerYearSubjectRisks", x => x.LearnerYearSubjectRiskId);
                    table.ForeignKey(
                        name: "FK_LearnerYearSubjectRisks_Learners_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "Learners",
                        principalColumn: "LearnerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearnerYearSubjectRisks_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerYearSubjectRisks_LearnerId_SubjectId",
                table: "LearnerYearSubjectRisks",
                columns: new[] { "LearnerId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerYearSubjectRisks_SubjectId",
                table: "LearnerYearSubjectRisks",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnerYearSubjectRisks");
        }
    }
}
