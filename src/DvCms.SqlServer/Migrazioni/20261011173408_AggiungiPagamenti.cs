using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiPagamenti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventiWebhook",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdEvento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Esito = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RicevutoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventiWebhook", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventiWebhook_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TentativiPagamento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdOrdine = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdProvider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Importo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Valuta = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdCattura = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TentativiPagamento", x => x.Id);
                    table.CheckConstraint("CK_TentativiPagamento_Importo", "[Importo] > 0");
                    table.ForeignKey(
                        name: "FK_TentativiPagamento_Ordini_IdTenant_IdSito_IdOrdine",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdOrdine },
                        principalTable: "Ordini",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventiWebhook_IdSito_Provider_IdEvento",
                table: "EventiWebhook",
                columns: new[] { "IdSito", "Provider", "IdEvento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventiWebhook_IdTenant_IdSito",
                table: "EventiWebhook",
                columns: new[] { "IdTenant", "IdSito" });

            migrationBuilder.CreateIndex(
                name: "IX_TentativiPagamento_IdOrdine",
                table: "TentativiPagamento",
                column: "IdOrdine");

            migrationBuilder.CreateIndex(
                name: "IX_TentativiPagamento_IdSito_Provider_IdProvider",
                table: "TentativiPagamento",
                columns: new[] { "IdSito", "Provider", "IdProvider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TentativiPagamento_IdTenant_IdSito_IdOrdine",
                table: "TentativiPagamento",
                columns: new[] { "IdTenant", "IdSito", "IdOrdine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventiWebhook");

            migrationBuilder.DropTable(
                name: "TentativiPagamento");
        }
    }
}
