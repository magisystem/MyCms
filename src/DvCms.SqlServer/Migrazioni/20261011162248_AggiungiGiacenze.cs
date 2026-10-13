using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiGiacenze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PrenotazioneScadeIlUtc",
                table: "Ordini",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GiacenzeVarianti",
                columns: table => new
                {
                    IdVariante = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Disponibile = table.Column<int>(type: "int", nullable: false),
                    Riservata = table.Column<int>(type: "int", nullable: false),
                    AggiornataIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiacenzeVarianti", x => x.IdVariante);
                    table.CheckConstraint("CK_GiacenzeVarianti_Quantita", "[Disponibile] >= 0 AND [Riservata] >= 0 AND [Riservata] <= [Disponibile] AND [Disponibile] <= 1000000");
                    table.ForeignKey(
                        name: "FK_GiacenzeVarianti_VariantiProdotto_IdTenant_IdSito_IdVariante",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdVariante },
                        principalTable: "VariantiProdotto",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentiGiacenza",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdVariante = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Variazione = table.Column<int>(type: "int", nullable: false),
                    DisponibileDopo = table.Column<int>(type: "int", nullable: false),
                    IdOrdine = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdAttore = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentiGiacenza", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentiGiacenza_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrenotazioniGiacenza",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdOrdine = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdVariante = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantita = table.Column<int>(type: "int", nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScadeIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornataIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrenotazioniGiacenza", x => x.Id);
                    table.CheckConstraint("CK_PrenotazioniGiacenza_Quantita", "[Quantita] BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_PrenotazioniGiacenza_Ordini_IdTenant_IdSito_IdOrdine",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdOrdine },
                        principalTable: "Ordini",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GiacenzeVarianti_IdTenant_IdSito_IdVariante",
                table: "GiacenzeVarianti",
                columns: new[] { "IdTenant", "IdSito", "IdVariante" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentiGiacenza_IdTenant_IdSito",
                table: "MovimentiGiacenza",
                columns: new[] { "IdTenant", "IdSito" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentiGiacenza_IdVariante_CreatoIlUtc",
                table: "MovimentiGiacenza",
                columns: new[] { "IdVariante", "CreatoIlUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrenotazioniGiacenza_IdOrdine_IdVariante",
                table: "PrenotazioniGiacenza",
                columns: new[] { "IdOrdine", "IdVariante" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrenotazioniGiacenza_IdTenant_IdSito_IdOrdine",
                table: "PrenotazioniGiacenza",
                columns: new[] { "IdTenant", "IdSito", "IdOrdine" });

            migrationBuilder.CreateIndex(
                name: "IX_PrenotazioniGiacenza_ScadeIlUtc",
                table: "PrenotazioniGiacenza",
                column: "ScadeIlUtc",
                filter: "[Stato] = 'Attiva'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GiacenzeVarianti");

            migrationBuilder.DropTable(
                name: "MovimentiGiacenza");

            migrationBuilder.DropTable(
                name: "PrenotazioniGiacenza");

            migrationBuilder.DropColumn(
                name: "PrenotazioneScadeIlUtc",
                table: "Ordini");
        }
    }
}
