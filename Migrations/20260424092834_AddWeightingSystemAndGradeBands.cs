using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ARIS1.Migrations
{
    /// <inheritdoc />
    public partial class AddWeightingSystemAndGradeBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GradeBands",
                columns: table => new
                {
                    GradeBandId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    MinPercentage = table.Column<float>(type: "real", nullable: false),
                    MaxPercentage = table.Column<float>(type: "real", nullable: false),
                    APSLevel = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeBands", x => x.GradeBandId);
                    table.ForeignKey(
                        name: "FK_GradeBands_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeightingStructures",
                columns: table => new
                {
                    WeightingStructureId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    Term = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightingStructures", x => x.WeightingStructureId);
                    table.ForeignKey(
                        name: "FK_WeightingStructures_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeightingNodes",
                columns: table => new
                {
                    WeightingNodeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WeightingStructureId = table.Column<int>(type: "int", nullable: false),
                    ParentNodeId = table.Column<int>(type: "int", nullable: true),
                    NodeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Weighting = table.Column<float>(type: "real", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    AssessmentTypeId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightingNodes", x => x.WeightingNodeId);
                    table.ForeignKey(
                        name: "FK_WeightingNodes_AssessmentTypes_AssessmentTypeId",
                        column: x => x.AssessmentTypeId,
                        principalTable: "AssessmentTypes",
                        principalColumn: "AssessmentTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeightingNodes_WeightingNodes_ParentNodeId",
                        column: x => x.ParentNodeId,
                        principalTable: "WeightingNodes",
                        principalColumn: "WeightingNodeId");
                    table.ForeignKey(
                        name: "FK_WeightingNodes_WeightingStructures_WeightingStructureId",
                        column: x => x.WeightingStructureId,
                        principalTable: "WeightingStructures",
                        principalColumn: "WeightingStructureId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GradeBands_SubjectId_MinPercentage_MaxPercentage",
                table: "GradeBands",
                columns: new[] { "SubjectId", "MinPercentage", "MaxPercentage" });

            migrationBuilder.CreateIndex(
                name: "IX_WeightingNodes_AssessmentTypeId",
                table: "WeightingNodes",
                column: "AssessmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightingNodes_ParentNodeId",
                table: "WeightingNodes",
                column: "ParentNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightingNodes_WeightingStructureId",
                table: "WeightingNodes",
                column: "WeightingStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightingStructures_SubjectId_Term",
                table: "WeightingStructures",
                columns: new[] { "SubjectId", "Term" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradeBands");

            migrationBuilder.DropTable(
                name: "WeightingNodes");

            migrationBuilder.DropTable(
                name: "WeightingStructures");
        }
    }
}
