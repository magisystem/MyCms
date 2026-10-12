using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiIndiceRicercaProdotti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TerminiRicercaProdotto",
                columns: table => new
                {
                    IdProdotto = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Campo = table.Column<byte>(type: "tinyint", nullable: false),
                    Termine = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminiRicercaProdotto", x => new { x.IdProdotto, x.IdLingua, x.Campo, x.Termine });
                    table.ForeignKey(
                        name: "FK_TerminiRicercaProdotto_Prodotti_IdTenant_IdSito_IdProdotto",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdProdotto },
                        principalTable: "Prodotti",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TerminiRicercaProdotto_IdSito_Termine",
                table: "TerminiRicercaProdotto",
                columns: new[] { "IdSito", "Termine" })
                .Annotation("SqlServer:Include", new[] { "IdProdotto", "IdLingua", "Campo" });

            migrationBuilder.CreateIndex(
                name: "IX_TerminiRicercaProdotto_IdTenant_IdSito_IdProdotto",
                table: "TerminiRicercaProdotto",
                columns: new[] { "IdTenant", "IdSito", "IdProdotto" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TerminiRicercaProdotto");
        }
    }
}
