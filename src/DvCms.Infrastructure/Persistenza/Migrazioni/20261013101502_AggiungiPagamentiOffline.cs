using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiPagamentiOffline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Ordini_Totali",
                table: "Ordini");

            migrationBuilder.AddColumn<decimal>(
                name: "CommissioneImponibile",
                table: "Ordini",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissioneIva",
                table: "Ordini",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissioneLorda",
                table: "Ordini",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ModalitaPagamento",
                table: "Ordini",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Online");

            migrationBuilder.CreateTable(
                name: "ImpostazioniPagamentoOfflineSiti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BonificoAttivo = table.Column<bool>(type: "bit", nullable: false),
                    Intestatario = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    Bic = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    NomeBanca = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GiorniPerPagare = table.Column<int>(type: "int", nullable: false),
                    IstruzioniBonifico = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContrassegnoAttivo = table.Column<bool>(type: "bit", nullable: false),
                    CommissioneFissa = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CommissionePercentuale = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CommissioneGratisSopra = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OrdineMinimo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OrdineMassimo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IstruzioniContrassegno = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpostazioniPagamentoOfflineSiti", x => x.Id);
                    table.CheckConstraint("CK_ImpostazioniPagamentoOfflineSiti_Commissione", "[CommissioneFissa] BETWEEN 0 AND 100000 AND [CommissionePercentuale] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_ImpostazioniPagamentoOfflineSiti_Giorni", "[GiorniPerPagare] BETWEEN 1 AND 60");
                    table.CheckConstraint("CK_ImpostazioniPagamentoOfflineSiti_Limiti", "([OrdineMinimo] IS NULL OR [OrdineMinimo] >= 0) AND ([OrdineMassimo] IS NULL OR [OrdineMassimo] >= 0) AND ([OrdineMinimo] IS NULL OR [OrdineMassimo] IS NULL OR [OrdineMinimo] <= [OrdineMassimo]) AND ([CommissioneGratisSopra] IS NULL OR [CommissioneGratisSopra] >= 0)");
                    table.ForeignKey(
                        name: "FK_ImpostazioniPagamentoOfflineSiti_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImpostazioniPagamentoOfflineSiti_Utenze_AggiornatoDaIdUtenza",
                        column: x => x.AggiornatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ordini_Commissione",
                table: "Ordini",
                sql: "[CommissioneLorda] = 0 OR [ModalitaPagamento] = 'Contrassegno'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ordini_Totali",
                table: "Ordini",
                sql: "[TotaleLordo] = [TotaleImponibile] + [TotaleIva] AND [SpedizioneLorda] = [SpedizioneImponibile] + [SpedizioneIva] AND [CommissioneLorda] = [CommissioneImponibile] + [CommissioneIva] AND [TotaleLordo] >= [SpedizioneLorda] + [CommissioneLorda]");

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniPagamentoOfflineSiti_AggiornatoDaIdUtenza",
                table: "ImpostazioniPagamentoOfflineSiti",
                column: "AggiornatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniPagamentoOfflineSiti_IdSito",
                table: "ImpostazioniPagamentoOfflineSiti",
                column: "IdSito",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniPagamentoOfflineSiti_IdTenant_IdSito",
                table: "ImpostazioniPagamentoOfflineSiti",
                columns: new[] { "IdTenant", "IdSito" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImpostazioniPagamentoOfflineSiti");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ordini_Commissione",
                table: "Ordini");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ordini_Totali",
                table: "Ordini");

            migrationBuilder.DropColumn(
                name: "CommissioneImponibile",
                table: "Ordini");

            migrationBuilder.DropColumn(
                name: "CommissioneIva",
                table: "Ordini");

            migrationBuilder.DropColumn(
                name: "CommissioneLorda",
                table: "Ordini");

            migrationBuilder.DropColumn(
                name: "ModalitaPagamento",
                table: "Ordini");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ordini_Totali",
                table: "Ordini",
                sql: "[TotaleLordo] = [TotaleImponibile] + [TotaleIva] AND [SpedizioneLorda] = [SpedizioneImponibile] + [SpedizioneIva] AND [TotaleLordo] >= [SpedizioneLorda]");
        }
    }
}
