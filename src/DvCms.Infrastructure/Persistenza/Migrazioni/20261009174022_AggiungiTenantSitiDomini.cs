using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiTenantSitiDomini : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Primario = table.Column<bool>(type: "bit", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfiliGuest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfiliGuest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfiliGuest_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Siti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FusoOrario = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RegistrazioneAcquirentiAbilitata = table.Column<bool>(type: "bit", nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Siti", x => x.Id);
                    table.UniqueConstraint("AK_Siti_IdTenant_Id", x => new { x.IdTenant, x.Id });
                    table.ForeignKey(
                        name: "FK_Siti_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DominiSito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    SoloSviluppo = table.Column<bool>(type: "bit", nullable: false),
                    Attivo = table.Column<bool>(type: "bit", nullable: false),
                    VerificatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerificatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DominiSito", x => x.Id);
                    table.CheckConstraint("CK_DominiSito_HostMinuscolo", "[Host] COLLATE Latin1_General_BIN2 = LOWER([Host]) COLLATE Latin1_General_BIN2");
                    table.ForeignKey(
                        name: "FK_DominiSito_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DominiSito_Host",
                table: "DominiSito",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DominiSito_IdTenant_IdSito",
                table: "DominiSito",
                columns: new[] { "IdTenant", "IdSito" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfiliGuest_IdTenant",
                table: "ProfiliGuest",
                column: "IdTenant",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenant_Primario",
                table: "Tenant",
                column: "Primario",
                unique: true,
                filter: "[Primario] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DominiSito");

            migrationBuilder.DropTable(
                name: "ProfiliGuest");

            migrationBuilder.DropTable(
                name: "Siti");

            migrationBuilder.DropTable(
                name: "Tenant");
        }
    }
}
