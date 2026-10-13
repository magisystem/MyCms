using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiOrdini : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UltimoNumeroOrdine",
                table: "Siti",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Ordini",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    IdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Valuta = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    TotaleLordo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotaleImponibile = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotaleIva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TipoSpedizione = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AreaSpedizione = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NomeSpedizione = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SpedizioneLorda = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SpedizioneImponibile = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SpedizioneIva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmailAcquirente = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    NomeAcquirente = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Destinatario = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IndirizzoRiga1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IndirizzoRiga2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Cap = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Citta = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Paese = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ordini", x => x.Id);
                    table.UniqueConstraint("AK_Ordini_IdTenant_IdSito_Id", x => new { x.IdTenant, x.IdSito, x.Id });
                    table.CheckConstraint("CK_Ordini_Consegna", "([TipoSpedizione] = 'Ritiro' AND [Paese] IS NULL AND [AreaSpedizione] IS NULL AND [SpedizioneLorda] = 0) OR ([TipoSpedizione] = 'Zona' AND [Paese] IS NOT NULL AND [AreaSpedizione] IS NOT NULL)");
                    table.CheckConstraint("CK_Ordini_Totali", "[TotaleLordo] = [TotaleImponibile] + [TotaleIva] AND [SpedizioneLorda] = [SpedizioneImponibile] + [SpedizioneIva] AND [TotaleLordo] >= [SpedizioneLorda]");
                    table.ForeignKey(
                        name: "FK_Ordini_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ordini_Utenze_IdUtenza",
                        column: x => x.IdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RigheOrdine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdOrdine = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Posizione = table.Column<int>(type: "int", nullable: false),
                    IdVariante = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NomeProdotto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EtichettaVariante = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Quantita = table.Column<int>(type: "int", nullable: false),
                    PrezzoUnitarioLordo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AliquotaIva = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Lordo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Imponibile = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RigheOrdine", x => x.Id);
                    table.CheckConstraint("CK_RigheOrdine_Importi", "[Lordo] = [Imponibile] + [Iva] AND [Lordo] = [PrezzoUnitarioLordo] * [Quantita]");
                    table.CheckConstraint("CK_RigheOrdine_Quantita", "[Quantita] BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_RigheOrdine_Ordini_IdTenant_IdSito_IdOrdine",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdOrdine },
                        principalTable: "Ordini",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ordini_IdSito_IdUtenza_CreatoIlUtc",
                table: "Ordini",
                columns: new[] { "IdSito", "IdUtenza", "CreatoIlUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Ordini_IdSito_Numero",
                table: "Ordini",
                columns: new[] { "IdSito", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ordini_IdUtenza",
                table: "Ordini",
                column: "IdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_RigheOrdine_IdOrdine_Posizione",
                table: "RigheOrdine",
                columns: new[] { "IdOrdine", "Posizione" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RigheOrdine_IdTenant_IdSito_IdOrdine",
                table: "RigheOrdine",
                columns: new[] { "IdTenant", "IdSito", "IdOrdine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RigheOrdine");

            migrationBuilder.DropTable(
                name: "Ordini");

            migrationBuilder.DropColumn(
                name: "UltimoNumeroOrdine",
                table: "Siti");
        }
    }
}
