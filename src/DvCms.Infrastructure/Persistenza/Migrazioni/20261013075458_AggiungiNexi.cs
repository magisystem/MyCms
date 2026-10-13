using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiNexi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HashTokenNotifica",
                table: "TentativiPagamento",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImpostazioniNexiSiti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ambiente = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SegretiProtetti = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpostazioniNexiSiti", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImpostazioniNexiSiti_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImpostazioniNexiSiti_Utenze_AggiornatoDaIdUtenza",
                        column: x => x.AggiornatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniNexiSiti_AggiornatoDaIdUtenza",
                table: "ImpostazioniNexiSiti",
                column: "AggiornatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniNexiSiti_IdSito",
                table: "ImpostazioniNexiSiti",
                column: "IdSito",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniNexiSiti_IdTenant_IdSito",
                table: "ImpostazioniNexiSiti",
                columns: new[] { "IdTenant", "IdSito" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImpostazioniNexiSiti");

            migrationBuilder.DropColumn(
                name: "HashTokenNotifica",
                table: "TentativiPagamento");
        }
    }
}
