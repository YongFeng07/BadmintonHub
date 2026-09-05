using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadmintonHub.Migrations
{
    /// <inheritdoc />
    public partial class P3_CategoryAndFacilityPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courts_Facilities_FacilityId",
                table: "Courts");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Facilities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UnitLabel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacilityPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FacilityId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityPhotos_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Pre-existing databases already hold facilities; give them a default
            // category so the new foreign key can be created (fresh databases are
            // enriched by the app seeder, which upserts categories by name).
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM Categories)
BEGIN
    SET IDENTITY_INSERT Categories ON;
    INSERT INTO Categories (Id, Name, UnitLabel, Icon, Status, DisplayOrder)
    VALUES (1, N'Badminton Court', N'Court', N'🏸', 0, 1);
    SET IDENTITY_INSERT Categories OFF;
END;
UPDATE Facilities SET CategoryId = 1 WHERE CategoryId = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_CategoryId",
                table: "Facilities",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilityPhotos_FacilityId",
                table: "FacilityPhotos",
                column: "FacilityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Courts_Facilities_FacilityId",
                table: "Courts",
                column: "FacilityId",
                principalTable: "Facilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Facilities_Categories_CategoryId",
                table: "Facilities",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courts_Facilities_FacilityId",
                table: "Courts");

            migrationBuilder.DropForeignKey(
                name: "FK_Facilities_Categories_CategoryId",
                table: "Facilities");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "FacilityPhotos");

            migrationBuilder.DropIndex(
                name: "IX_Facilities_CategoryId",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Facilities");

            migrationBuilder.AddForeignKey(
                name: "FK_Courts_Facilities_FacilityId",
                table: "Courts",
                column: "FacilityId",
                principalTable: "Facilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
