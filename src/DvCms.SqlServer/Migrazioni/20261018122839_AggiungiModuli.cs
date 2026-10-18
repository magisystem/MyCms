using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiModuli : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Moduli",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefinizioneJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImpostazioniJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GiorniConservazione = table.Column<int>(type: "int", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moduli", x => x.Id);
                    table.UniqueConstraint("AK_Moduli_IdTenant_IdSito_Id", x => new { x.IdTenant, x.IdSito, x.Id });
                    table.CheckConstraint("CK_Moduli_GiorniConservazione", "[GiorniConservazione] BETWEEN 30 AND 1825");
                    table.ForeignKey(
                        name: "FK_Moduli_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Moduli_Utenze_AggiornatoDaIdUtenza",
                        column: x => x.AggiornatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InviiModulo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdModulo = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodiceLingua = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContenutoJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InviiModulo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InviiModulo_Moduli_IdTenant_IdSito_IdModulo",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdModulo },
                        principalTable: "Moduli",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InviiModulo_CreatoIlUtc",
                table: "InviiModulo",
                column: "CreatoIlUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InviiModulo_IdSito_IdModulo_CreatoIlUtc",
                table: "InviiModulo",
                columns: new[] { "IdSito", "IdModulo", "CreatoIlUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InviiModulo_IdTenant_IdSito_IdModulo",
                table: "InviiModulo",
                columns: new[] { "IdTenant", "IdSito", "IdModulo" });

            migrationBuilder.CreateIndex(
                name: "IX_Moduli_AggiornatoDaIdUtenza",
                table: "Moduli",
                column: "AggiornatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_Moduli_IdSito_Nome",
                table: "Moduli",
                columns: new[] { "IdSito", "Nome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InviiModulo");

            migrationBuilder.DropTable(
                name: "Moduli");
        }
    }
}
