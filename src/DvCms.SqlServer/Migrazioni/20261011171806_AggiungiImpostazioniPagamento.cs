using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiImpostazioniPagamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImpostazioniPagamentoSiti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ambiente = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdClient = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SegretiProtetti = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpostazioniPagamentoSiti", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImpostazioniPagamentoSiti_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImpostazioniPagamentoSiti_Utenze_AggiornatoDaIdUtenza",
                        column: x => x.AggiornatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniPagamentoSiti_AggiornatoDaIdUtenza",
                table: "ImpostazioniPagamentoSiti",
                column: "AggiornatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniPagamentoSiti_IdSito",
                table: "ImpostazioniPagamentoSiti",
                column: "IdSito",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniPagamentoSiti_IdTenant_IdSito",
                table: "ImpostazioniPagamentoSiti",
                columns: new[] { "IdTenant", "IdSito" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImpostazioniPagamentoSiti");
        }
    }
}
