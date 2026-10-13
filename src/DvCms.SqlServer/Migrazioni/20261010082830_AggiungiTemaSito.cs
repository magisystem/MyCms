using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiTemaSito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImpostazioniTemaSito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChiaveTema = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VersioneTema = table.Column<int>(type: "int", nullable: false),
                    ImpostazioniJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpostazioniTemaSito", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImpostazioniTemaSito_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImpostazioniTemaSito_Utenze_AggiornatoDaIdUtenza",
                        column: x => x.AggiornatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniTemaSito_AggiornatoDaIdUtenza",
                table: "ImpostazioniTemaSito",
                column: "AggiornatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniTemaSito_IdSito",
                table: "ImpostazioniTemaSito",
                column: "IdSito",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniTemaSito_IdTenant_IdSito",
                table: "ImpostazioniTemaSito",
                columns: new[] { "IdTenant", "IdSito" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImpostazioniTemaSito");
        }
    }
}
