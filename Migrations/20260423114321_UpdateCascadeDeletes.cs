using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ARIS1.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCascadeDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssessmentQuestions_Assessments_AssessmentId",
                table: "AssessmentQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_Interventions_Learners_LearnerId",
                table: "Interventions");

            migrationBuilder.DropForeignKey(
                name: "FK_LearnerQuestionMarks_AssessmentQuestions_QuestionId",
                table: "LearnerQuestionMarks");

            migrationBuilder.AddForeignKey(
                name: "FK_AssessmentQuestions_Assessments_AssessmentId",
                table: "AssessmentQuestions",
                column: "AssessmentId",
                principalTable: "Assessments",
                principalColumn: "AssessmentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Interventions_Learners_LearnerId",
                table: "Interventions",
                column: "LearnerId",
                principalTable: "Learners",
                principalColumn: "LearnerId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LearnerQuestionMarks_AssessmentQuestions_QuestionId",
                table: "LearnerQuestionMarks",
                column: "QuestionId",
                principalTable: "AssessmentQuestions",
                principalColumn: "QuestionId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssessmentQuestions_Assessments_AssessmentId",
                table: "AssessmentQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_Interventions_Learners_LearnerId",
                table: "Interventions");

            migrationBuilder.DropForeignKey(
                name: "FK_LearnerQuestionMarks_AssessmentQuestions_QuestionId",
                table: "LearnerQuestionMarks");

            migrationBuilder.AddForeignKey(
                name: "FK_AssessmentQuestions_Assessments_AssessmentId",
                table: "AssessmentQuestions",
                column: "AssessmentId",
                principalTable: "Assessments",
                principalColumn: "AssessmentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Interventions_Learners_LearnerId",
                table: "Interventions",
                column: "LearnerId",
                principalTable: "Learners",
                principalColumn: "LearnerId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LearnerQuestionMarks_AssessmentQuestions_QuestionId",
                table: "LearnerQuestionMarks",
                column: "QuestionId",
                principalTable: "AssessmentQuestions",
                principalColumn: "QuestionId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
