using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiRimborsi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ImportoIncassato",
                table: "TentativiPagamento",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotaleRimborsato",
                table: "Ordini",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Rimborsi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdOrdine = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTentativo = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncassoOrfano = table.Column<bool>(type: "bit", nullable: false),
                    Importo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Valuta = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdProvider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IdUtenzaRichiedente = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rimborsi", x => x.Id);
                    table.CheckConstraint("CK_Rimborsi_Importo", "[Importo] > 0");
                    table.ForeignKey(
                        name: "FK_Rimborsi_Ordini_IdTenant_IdSito_IdOrdine",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdOrdine },
                        principalTable: "Ordini",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rimborsi_TentativiPagamento_IdTentativo",
                        column: x => x.IdTentativo,
                        principalTable: "TentativiPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rimborsi_Utenze_IdUtenzaRichiedente",
                        column: x => x.IdUtenzaRichiedente,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ordini_Rimborsato",
                table: "Ordini",
                sql: "[TotaleRimborsato] >= 0 AND [TotaleRimborsato] <= [TotaleLordo]");

            migrationBuilder.CreateIndex(
                name: "IX_Rimborsi_IdOrdine",
                table: "Rimborsi",
                column: "IdOrdine");

            migrationBuilder.CreateIndex(
                name: "IX_Rimborsi_IdSito_IdProvider",
                table: "Rimborsi",
                columns: new[] { "IdSito", "IdProvider" },
                filter: "[IdProvider] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Rimborsi_IdTenant_IdSito_IdOrdine",
                table: "Rimborsi",
                columns: new[] { "IdTenant", "IdSito", "IdOrdine" });

            migrationBuilder.CreateIndex(
                name: "IX_Rimborsi_IdTentativo",
                table: "Rimborsi",
                column: "IdTentativo");

            migrationBuilder.CreateIndex(
                name: "IX_Rimborsi_IdUtenzaRichiedente",
                table: "Rimborsi",
                column: "IdUtenzaRichiedente");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Rimborsi");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ordini_Rimborsato",
                table: "Ordini");

            migrationBuilder.DropColumn(
                name: "ImportoIncassato",
                table: "TentativiPagamento");

            migrationBuilder.DropColumn(
                name: "TotaleRimborsato",
                table: "Ordini");
        }
    }
}
