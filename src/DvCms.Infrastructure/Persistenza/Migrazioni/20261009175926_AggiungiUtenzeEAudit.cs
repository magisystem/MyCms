using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiUtenzeEAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Utenze",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Ruolo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    EmailNormalizzata = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    NomeVisualizzato = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HashPassword = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordAggiornataIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeveCambiarePassword = table.Column<bool>(type: "bit", nullable: false),
                    TimbroSicurezza = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContatoreAccessiFalliti = table.Column<int>(type: "int", nullable: false),
                    BloccatoFinoAlUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimoAccessoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Utenze", x => x.Id);
                    table.CheckConstraint("CK_Utenze_RuoloETenant", "([Ruolo] = 'PlatformAdmin' AND [IdTenant] IS NULL) OR ([Ruolo] <> 'PlatformAdmin' AND [IdTenant] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Utenze_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Utenze_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegistroAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdUtenzaAttore = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Azione = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Risorsa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Esito = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Dettagli = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistroAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistroAudit_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegistroAudit_Utenze_IdUtenzaAttore",
                        column: x => x.IdUtenzaAttore,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestiStatici_AggiornatoDaIdUtenza",
                table: "TestiStatici",
                column: "AggiornatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_DominiSito_VerificatoDaIdUtenza",
                table: "DominiSito",
                column: "VerificatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_RegistroAudit_IdTenant_CreatoIlUtc",
                table: "RegistroAudit",
                columns: new[] { "IdTenant", "CreatoIlUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistroAudit_IdUtenzaAttore",
                table: "RegistroAudit",
                column: "IdUtenzaAttore");

            migrationBuilder.CreateIndex(
                name: "IX_Utenze_IdLingua",
                table: "Utenze",
                column: "IdLingua");

            migrationBuilder.CreateIndex(
                name: "IX_Utenze_IdTenant_EmailNormalizzata",
                table: "Utenze",
                columns: new[] { "IdTenant", "EmailNormalizzata" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DominiSito_Utenze_VerificatoDaIdUtenza",
                table: "DominiSito",
                column: "VerificatoDaIdUtenza",
                principalTable: "Utenze",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TestiStatici_Utenze_AggiornatoDaIdUtenza",
                table: "TestiStatici",
                column: "AggiornatoDaIdUtenza",
                principalTable: "Utenze",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DominiSito_Utenze_VerificatoDaIdUtenza",
                table: "DominiSito");

            migrationBuilder.DropForeignKey(
                name: "FK_TestiStatici_Utenze_AggiornatoDaIdUtenza",
                table: "TestiStatici");

            migrationBuilder.DropTable(
                name: "RegistroAudit");

            migrationBuilder.DropTable(
                name: "Utenze");

            migrationBuilder.DropIndex(
                name: "IX_TestiStatici_AggiornatoDaIdUtenza",
                table: "TestiStatici");

            migrationBuilder.DropIndex(
                name: "IX_DominiSito_VerificatoDaIdUtenza",
                table: "DominiSito");
        }
    }
}
