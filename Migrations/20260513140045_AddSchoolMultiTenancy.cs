using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ARIS1.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubjectId1",
                table: "WeightingStructures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "Subjects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubjectId1",
                table: "GradeBands",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Schools",
                columns: table => new
                {
                    SchoolId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schools", x => x.SchoolId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeightingStructures_SubjectId1",
                table: "WeightingStructures",
                column: "SubjectId1");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_SchoolId",
                table: "Subjects",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeBands_SubjectId1",
                table: "GradeBands",
                column: "SubjectId1");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SchoolId",
                table: "AspNetUsers",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Schools_Code",
                table: "Schools",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Schools_SchoolId",
                table: "AspNetUsers",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "SchoolId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GradeBands_Subjects_SubjectId1",
                table: "GradeBands",
                column: "SubjectId1",
                principalTable: "Subjects",
                principalColumn: "SubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Schools_SchoolId",
                table: "Subjects",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "SchoolId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WeightingStructures_Subjects_SubjectId1",
                table: "WeightingStructures",
                column: "SubjectId1",
                principalTable: "Subjects",
                principalColumn: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Schools_SchoolId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_GradeBands_Subjects_SubjectId1",
                table: "GradeBands");

            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Schools_SchoolId",
                table: "Subjects");

            migrationBuilder.DropForeignKey(
                name: "FK_WeightingStructures_Subjects_SubjectId1",
                table: "WeightingStructures");

            migrationBuilder.DropTable(
                name: "Schools");

            migrationBuilder.DropIndex(
                name: "IX_WeightingStructures_SubjectId1",
                table: "WeightingStructures");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_SchoolId",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_GradeBands_SubjectId1",
                table: "GradeBands");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SchoolId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SubjectId1",
                table: "WeightingStructures");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "SubjectId1",
                table: "GradeBands");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "AspNetUsers");
        }
    }
}
