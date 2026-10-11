using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiMetodiSpedizione : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetodiSpedizione",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TariffaLorda = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Attivo = table.Column<bool>(type: "bit", nullable: false),
                    Posizione = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetodiSpedizione", x => x.Id);
                    table.CheckConstraint("CK_MetodiSpedizione_TipoEArea", "([Tipo] = 'Ritiro' AND [Area] IS NULL AND [TariffaLorda] = 0) OR ([Tipo] = 'Zona' AND [Area] IS NOT NULL AND [TariffaLorda] >= 0)");
                    table.ForeignKey(
                        name: "FK_MetodiSpedizione_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetodiSpedizione_IdSito_Posizione",
                table: "MetodiSpedizione",
                columns: new[] { "IdSito", "Posizione" });

            migrationBuilder.CreateIndex(
                name: "IX_MetodiSpedizione_IdTenant_IdSito",
                table: "MetodiSpedizione",
                columns: new[] { "IdTenant", "IdSito" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetodiSpedizione");
        }
    }
}
