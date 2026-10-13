using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiConnessioniEmailOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnessioniEmailOAuth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ImprontaApp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Casella = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    TokenAggiornamentoProtetto = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TokenAccessoProtetto = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScadenzaAccessoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CollegataIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CollegataDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UltimoRinnovoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimoErrore = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UltimoErroreIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnessioniEmailOAuth", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnessioniEmailOAuth_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConnessioniEmailOAuth_Utenze_CollegataDaIdUtenza",
                        column: x => x.CollegataDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnessioniEmailOAuth_CollegataDaIdUtenza",
                table: "ConnessioniEmailOAuth",
                column: "CollegataDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_ConnessioniEmailOAuth_IdTenant",
                table: "ConnessioniEmailOAuth",
                column: "IdTenant",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnessioniEmailOAuth");
        }
    }
}
