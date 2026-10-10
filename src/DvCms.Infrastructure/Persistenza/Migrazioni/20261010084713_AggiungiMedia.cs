using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RisorseMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChiaveOggetto = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NomeOriginale = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TipoContenuto = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Dimensione = table.Column<long>(type: "bigint", nullable: false),
                    HashSha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Larghezza = table.Column<int>(type: "int", nullable: true),
                    Altezza = table.Column<int>(type: "int", nullable: true),
                    Visibilita = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatoScansione = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RisorseMedia", x => x.Id);
                    table.UniqueConstraint("AK_RisorseMedia_IdTenant_IdSito_Id", x => new { x.IdTenant, x.IdSito, x.Id });
                    table.ForeignKey(
                        name: "FK_RisorseMedia_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RisorseMedia_Utenze_CreatoDaIdUtenza",
                        column: x => x.CreatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TraduzioniRisorseMedia",
                columns: table => new
                {
                    IdRisorsa = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestoAlternativo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraduzioniRisorseMedia", x => new { x.IdRisorsa, x.IdLingua });
                    table.ForeignKey(
                        name: "FK_TraduzioniRisorseMedia_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TraduzioniRisorseMedia_RisorseMedia_IdTenant_IdSito_IdRisorsa",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdRisorsa },
                        principalTable: "RisorseMedia",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RisorseMedia_ChiaveOggetto",
                table: "RisorseMedia",
                column: "ChiaveOggetto",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RisorseMedia_CreatoDaIdUtenza",
                table: "RisorseMedia",
                column: "CreatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_RisorseMedia_IdSito_CreatoIlUtc",
                table: "RisorseMedia",
                columns: new[] { "IdSito", "CreatoIlUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TraduzioniRisorseMedia_IdLingua",
                table: "TraduzioniRisorseMedia",
                column: "IdLingua");

            migrationBuilder.CreateIndex(
                name: "IX_TraduzioniRisorseMedia_IdTenant_IdSito_IdRisorsa",
                table: "TraduzioniRisorseMedia",
                columns: new[] { "IdTenant", "IdSito", "IdRisorsa" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TraduzioniRisorseMedia");

            migrationBuilder.DropTable(
                name: "RisorseMedia");
        }
    }
}
