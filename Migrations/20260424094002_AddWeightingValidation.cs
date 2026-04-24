using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ARIS1.Migrations
{
    /// <inheritdoc />
    public partial class AddWeightingValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeightingValidations",
                columns: table => new
                {
                    WeightingValidationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WeightingStructureId = table.Column<int>(type: "int", nullable: false),
                    NodePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CheckedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightingValidations", x => x.WeightingValidationId);
                    table.ForeignKey(
                        name: "FK_WeightingValidations_WeightingStructures_WeightingStructureId",
                        column: x => x.WeightingStructureId,
                        principalTable: "WeightingStructures",
                        principalColumn: "WeightingStructureId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeightingValidations_WeightingStructureId",
                table: "WeightingValidations",
                column: "WeightingStructureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeightingValidations");
        }
    }
}
