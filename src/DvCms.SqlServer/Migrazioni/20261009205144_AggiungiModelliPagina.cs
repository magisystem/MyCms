using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiModelliPagina : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DatiModelloJson",
                table: "RevisioniPagina",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "IdModello",
                table: "RevisioniPagina",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "testo-semplice");

            migrationBuilder.AddColumn<int>(
                name: "VersioneModello",
                table: "RevisioniPagina",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DatiModelloJson",
                table: "RevisioniPagina");

            migrationBuilder.DropColumn(
                name: "IdModello",
                table: "RevisioniPagina");

            migrationBuilder.DropColumn(
                name: "VersioneModello",
                table: "RevisioniPagina");
        }
    }
}
