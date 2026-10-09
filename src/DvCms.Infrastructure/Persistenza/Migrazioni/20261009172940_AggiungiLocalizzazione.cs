using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiLocalizzazione : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Lingue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Codice = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Cultura = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Predefinita = table.Column<bool>(type: "bit", nullable: false),
                    Attiva = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lingue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestiStatici",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chiave = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    HashChiave = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Testo = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DaTradurre = table.Column<bool>(type: "bit", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestiStatici", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestiStatici_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Lingue",
                columns: new[] { "Id", "Attiva", "Codice", "Cultura", "Nome", "Predefinita" },
                values: new object[,]
                {
                    { new Guid("01996a00-0000-7000-8000-000000000001"), true, "it", "it-IT", "Italiano", true },
                    { new Guid("01996a00-0000-7000-8000-000000000002"), true, "en", "en-GB", "English", false }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lingue_Codice",
                table: "Lingue",
                column: "Codice",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestiStatici_IdLingua_HashChiave",
                table: "TestiStatici",
                columns: new[] { "IdLingua", "HashChiave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestiStatici");

            migrationBuilder.DropTable(
                name: "Lingue");
        }
    }
}
