using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiCategorie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categorie",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Posizione = table.Column<int>(type: "int", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categorie", x => x.Id);
                    table.UniqueConstraint("AK_Categorie_IdTenant_IdSito_Id", x => new { x.IdTenant, x.IdSito, x.Id });
                    table.ForeignKey(
                        name: "FK_Categorie_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProdottiCategoria",
                columns: table => new
                {
                    IdProdotto = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdCategoria = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdottiCategoria", x => new { x.IdProdotto, x.IdCategoria });
                    table.ForeignKey(
                        name: "FK_ProdottiCategoria_Categorie_IdTenant_IdSito_IdCategoria",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdCategoria },
                        principalTable: "Categorie",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProdottiCategoria_Prodotti_IdTenant_IdSito_IdProdotto",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdProdotto },
                        principalTable: "Prodotti",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TraduzioniCategoria",
                columns: table => new
                {
                    IdCategoria = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraduzioniCategoria", x => new { x.IdCategoria, x.IdLingua });
                    table.ForeignKey(
                        name: "FK_TraduzioniCategoria_Categorie_IdTenant_IdSito_IdCategoria",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdCategoria },
                        principalTable: "Categorie",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TraduzioniCategoria_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categorie_IdSito_Posizione",
                table: "Categorie",
                columns: new[] { "IdSito", "Posizione" });

            migrationBuilder.CreateIndex(
                name: "IX_ProdottiCategoria_IdCategoria",
                table: "ProdottiCategoria",
                column: "IdCategoria");

            migrationBuilder.CreateIndex(
                name: "IX_ProdottiCategoria_IdTenant_IdSito_IdCategoria",
                table: "ProdottiCategoria",
                columns: new[] { "IdTenant", "IdSito", "IdCategoria" });

            migrationBuilder.CreateIndex(
                name: "IX_ProdottiCategoria_IdTenant_IdSito_IdProdotto",
                table: "ProdottiCategoria",
                columns: new[] { "IdTenant", "IdSito", "IdProdotto" });

            migrationBuilder.CreateIndex(
                name: "IX_TraduzioniCategoria_IdLingua",
                table: "TraduzioniCategoria",
                column: "IdLingua");

            migrationBuilder.CreateIndex(
                name: "IX_TraduzioniCategoria_IdTenant_IdSito_IdCategoria",
                table: "TraduzioniCategoria",
                columns: new[] { "IdTenant", "IdSito", "IdCategoria" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProdottiCategoria");

            migrationBuilder.DropTable(
                name: "TraduzioniCategoria");

            migrationBuilder.DropTable(
                name: "Categorie");
        }
    }
}
