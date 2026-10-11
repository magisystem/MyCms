using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiCatalogoProdotti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CommercioAttivo",
                table: "Siti",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Prodotti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AliquotaIva = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prodotti", x => x.Id);
                    table.UniqueConstraint("AK_Prodotti_IdTenant_IdSito_Id", x => new { x.IdTenant, x.IdSito, x.Id });
                    table.CheckConstraint("CK_Prodotti_AliquotaIva", "[AliquotaIva] IN (0, 4, 5, 10, 22)");
                    table.ForeignKey(
                        name: "FK_Prodotti_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TraduzioniProdotto",
                columns: table => new
                {
                    IdProdotto = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Descrizione = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraduzioniProdotto", x => new { x.IdProdotto, x.IdLingua });
                    table.ForeignKey(
                        name: "FK_TraduzioniProdotto_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TraduzioniProdotto_Prodotti_IdTenant_IdSito_IdProdotto",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdProdotto },
                        principalTable: "Prodotti",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VariantiProdotto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdProdotto = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PrezzoLordo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Valuta = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Attiva = table.Column<bool>(type: "bit", nullable: false),
                    Posizione = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantiProdotto", x => x.Id);
                    table.UniqueConstraint("AK_VariantiProdotto_IdTenant_IdSito_Id", x => new { x.IdTenant, x.IdSito, x.Id });
                    table.CheckConstraint("CK_VariantiProdotto_Prezzo", "[PrezzoLordo] > 0");
                    table.CheckConstraint("CK_VariantiProdotto_Valuta", "[Valuta] = 'EUR'");
                    table.ForeignKey(
                        name: "FK_VariantiProdotto_Prodotti_IdTenant_IdSito_IdProdotto",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdProdotto },
                        principalTable: "Prodotti",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TraduzioniVarianteProdotto",
                columns: table => new
                {
                    IdVariante = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Etichetta = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraduzioniVarianteProdotto", x => new { x.IdVariante, x.IdLingua });
                    table.ForeignKey(
                        name: "FK_TraduzioniVarianteProdotto_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TraduzioniVarianteProdotto_VariantiProdotto_IdTenant_IdSito_IdVariante",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdVariante },
                        principalTable: "VariantiProdotto",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Siti_RegistrazioneRichiedeCommercio",
                table: "Siti",
                sql: "[RegistrazioneAcquirentiAbilitata] = 0 OR [CommercioAttivo] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Prodotti_IdSito_Stato",
                table: "Prodotti",
                columns: new[] { "IdSito", "Stato" });

            migrationBuilder.CreateIndex(
                name: "IX_TraduzioniProdotto_IdLingua",
                table: "TraduzioniProdotto",
                column: "IdLingua");

            migrationBuilder.CreateIndex(
                name: "IX_TraduzioniProdotto_IdTenant_IdSito_IdProdotto",
                table: "TraduzioniProdotto",
                columns: new[] { "IdTenant", "IdSito", "IdProdotto" });

            migrationBuilder.CreateIndex(
                name: "IX_TraduzioniVarianteProdotto_IdLingua",
                table: "TraduzioniVarianteProdotto",
                column: "IdLingua");

            migrationBuilder.CreateIndex(
                name: "IX_TraduzioniVarianteProdotto_IdTenant_IdSito_IdVariante",
                table: "TraduzioniVarianteProdotto",
                columns: new[] { "IdTenant", "IdSito", "IdVariante" });

            migrationBuilder.CreateIndex(
                name: "IX_VariantiProdotto_IdProdotto",
                table: "VariantiProdotto",
                column: "IdProdotto");

            migrationBuilder.CreateIndex(
                name: "IX_VariantiProdotto_IdSito_Sku",
                table: "VariantiProdotto",
                columns: new[] { "IdSito", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariantiProdotto_IdTenant_IdSito_IdProdotto",
                table: "VariantiProdotto",
                columns: new[] { "IdTenant", "IdSito", "IdProdotto" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TraduzioniProdotto");

            migrationBuilder.DropTable(
                name: "TraduzioniVarianteProdotto");

            migrationBuilder.DropTable(
                name: "VariantiProdotto");

            migrationBuilder.DropTable(
                name: "Prodotti");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Siti_RegistrazioneRichiedeCommercio",
                table: "Siti");

            migrationBuilder.DropColumn(
                name: "CommercioAttivo",
                table: "Siti");
        }
    }
}
