using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiImpostazioniEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChiaviSegrete",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeAmichevole = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChiaviSegrete", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImpostazioniEmailTenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValoriJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SegretiProtetti = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpostazioniEmailTenant", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImpostazioniEmailTenant_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImpostazioniEmailTenant_Utenze_AggiornatoDaIdUtenza",
                        column: x => x.AggiornatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniEmailTenant_AggiornatoDaIdUtenza",
                table: "ImpostazioniEmailTenant",
                column: "AggiornatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_ImpostazioniEmailTenant_IdTenant",
                table: "ImpostazioniEmailTenant",
                column: "IdTenant",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChiaviSegrete");

            migrationBuilder.DropTable(
                name: "ImpostazioniEmailTenant");
        }
    }
}
