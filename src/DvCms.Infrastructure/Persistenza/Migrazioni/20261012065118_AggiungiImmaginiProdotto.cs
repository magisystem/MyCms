using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiImmaginiProdotto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImmaginiProdotto",
                columns: table => new
                {
                    IdProdotto = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdRisorsa = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Posizione = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImmaginiProdotto", x => new { x.IdProdotto, x.IdRisorsa });
                    table.ForeignKey(
                        name: "FK_ImmaginiProdotto_Prodotti_IdTenant_IdSito_IdProdotto",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdProdotto },
                        principalTable: "Prodotti",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImmaginiProdotto_IdProdotto_Posizione",
                table: "ImmaginiProdotto",
                columns: new[] { "IdProdotto", "Posizione" });

            migrationBuilder.CreateIndex(
                name: "IX_ImmaginiProdotto_IdTenant_IdSito_IdProdotto",
                table: "ImmaginiProdotto",
                columns: new[] { "IdTenant", "IdSito", "IdProdotto" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImmaginiProdotto");
        }
    }
}
