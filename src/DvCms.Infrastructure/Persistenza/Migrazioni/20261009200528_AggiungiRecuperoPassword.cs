using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiRecuperoPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RichiesteRecuperoPassword",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailNormalizzata = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    IdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IndirizzoIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HashRiferimento = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    SaleCodice = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    HashCodice = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ScadenzaCodiceIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TentativiCodice = table.Column<int>(type: "int", nullable: false),
                    HashToken = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    ScadenzaTokenIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RichiesteRecuperoPassword", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RichiesteRecuperoPassword_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RichiesteRecuperoPassword_Utenze_IdUtenza",
                        column: x => x.IdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteRecuperoPassword_HashRiferimento",
                table: "RichiesteRecuperoPassword",
                column: "HashRiferimento");

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteRecuperoPassword_HashToken",
                table: "RichiesteRecuperoPassword",
                column: "HashToken",
                filter: "[HashToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteRecuperoPassword_IdTenant_EmailNormalizzata_CreatoIlUtc",
                table: "RichiesteRecuperoPassword",
                columns: new[] { "IdTenant", "EmailNormalizzata", "CreatoIlUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteRecuperoPassword_IdUtenza",
                table: "RichiesteRecuperoPassword",
                column: "IdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteRecuperoPassword_IndirizzoIp_CreatoIlUtc",
                table: "RichiesteRecuperoPassword",
                columns: new[] { "IndirizzoIp", "CreatoIlUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RichiesteRecuperoPassword");
        }
    }
}
