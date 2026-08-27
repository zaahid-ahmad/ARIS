using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ARIS1.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: add ClassId as nullable first — ClassName stays in place so we can
            // backfill from it before dropping it.
            migrationBuilder.AddColumn<int>(
                name: "ClassId",
                table: "Learners",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchoolClasses",
                columns: table => new
                {
                    ClassId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SchoolId = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolClasses", x => x.ClassId);
                    table.ForeignKey(
                        name: "FK_SchoolClasses_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "SchoolId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolClasses_SchoolId_Grade_Name",
                table: "SchoolClasses",
                columns: new[] { "SchoolId", "Grade", "Name" },
                unique: true);

            // Step 2: backfill one SchoolClass row per distinct (school, grade, ClassName)
            // combination seen among existing learners. Where the legacy ClassName already
            // begins with the learner's own grade digits (e.g. "10A" under Grade 10), the
            // grade prefix is stripped so the new class's Name is just the short form ("A") —
            // matching what an admin will type going forward. A mismatched legacy value (e.g.
            // "10A" under Grade 11) keeps the full original string, since it doesn't start
            // with that grade's digits.
            migrationBuilder.Sql(@"
                INSERT INTO SchoolClasses (SchoolId, Grade, Name)
                SELECT DISTINCT
                    u.SchoolId,
                    l.Grade,
                    CASE
                        WHEN l.ClassName LIKE CAST(l.Grade AS NVARCHAR(10)) + '%'
                             AND LEN(l.ClassName) > LEN(CAST(l.Grade AS NVARCHAR(10)))
                        THEN SUBSTRING(l.ClassName, LEN(CAST(l.Grade AS NVARCHAR(10))) + 1, LEN(l.ClassName))
                        ELSE l.ClassName
                    END
                FROM Learners l
                JOIN AspNetUsers u ON l.UserId = u.Id
                WHERE u.SchoolId IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE l
                SET l.ClassId = sc.ClassId
                FROM Learners l
                JOIN AspNetUsers u ON l.UserId = u.Id
                JOIN SchoolClasses sc ON sc.SchoolId = u.SchoolId AND sc.Grade = l.Grade AND sc.Name = (
                    CASE
                        WHEN l.ClassName LIKE CAST(l.Grade AS NVARCHAR(10)) + '%'
                             AND LEN(l.ClassName) > LEN(CAST(l.Grade AS NVARCHAR(10)))
                        THEN SUBSTRING(l.ClassName, LEN(CAST(l.Grade AS NVARCHAR(10))) + 1, LEN(l.ClassName))
                        ELSE l.ClassName
                    END
                );
            ");

            // Step 3: now that every learner has a valid ClassId, enforce NOT NULL and drop
            // the legacy free-text column.
            migrationBuilder.AlterColumn<int>(
                name: "ClassId",
                table: "Learners",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ClassName",
                table: "Learners");

            migrationBuilder.CreateIndex(
                name: "IX_Learners_ClassId",
                table: "Learners",
                column: "ClassId");

            migrationBuilder.AddForeignKey(
                name: "FK_Learners_SchoolClasses_ClassId",
                table: "Learners",
                column: "ClassId",
                principalTable: "SchoolClasses",
                principalColumn: "ClassId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Learners_SchoolClasses_ClassId",
                table: "Learners");

            migrationBuilder.DropIndex(
                name: "IX_Learners_ClassId",
                table: "Learners");

            migrationBuilder.AddColumn<string>(
                name: "ClassName",
                table: "Learners",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // Best-effort reconstruction: Grade + Name (e.g. "10" + "A" -> "10A"). This does
            // not guarantee exact recovery of a pre-migration ClassName that didn't follow
            // that pattern, but is the only derivable mapping once SchoolClass is gone.
            migrationBuilder.Sql(@"
                UPDATE l
                SET l.ClassName = CAST(l.Grade AS NVARCHAR(10)) + sc.Name
                FROM Learners l
                JOIN SchoolClasses sc ON sc.ClassId = l.ClassId;
            ");

            migrationBuilder.DropColumn(
                name: "ClassId",
                table: "Learners");

            migrationBuilder.DropTable(
                name: "SchoolClasses");
        }
    }
}
