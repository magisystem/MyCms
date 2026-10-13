using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiCarrello : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carrelli",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carrelli", x => x.Id);
                    table.UniqueConstraint("AK_Carrelli_IdTenant_IdSito_Id", x => new { x.IdTenant, x.IdSito, x.Id });
                    table.ForeignKey(
                        name: "FK_Carrelli_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Carrelli_Utenze_IdUtenza",
                        column: x => x.IdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RigheCarrello",
                columns: table => new
                {
                    IdCarrello = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdVariante = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantita = table.Column<int>(type: "int", nullable: false),
                    AggiuntaIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RigheCarrello", x => new { x.IdCarrello, x.IdVariante });
                    table.CheckConstraint("CK_RigheCarrello_Quantita", "[Quantita] BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_RigheCarrello_Carrelli_IdTenant_IdSito_IdCarrello",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdCarrello },
                        principalTable: "Carrelli",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RigheCarrello_VariantiProdotto_IdTenant_IdSito_IdVariante",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdVariante },
                        principalTable: "VariantiProdotto",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carrelli_IdSito_IdUtenza",
                table: "Carrelli",
                columns: new[] { "IdSito", "IdUtenza" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carrelli_IdUtenza",
                table: "Carrelli",
                column: "IdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_RigheCarrello_IdTenant_IdSito_IdCarrello",
                table: "RigheCarrello",
                columns: new[] { "IdTenant", "IdSito", "IdCarrello" });

            migrationBuilder.CreateIndex(
                name: "IX_RigheCarrello_IdTenant_IdSito_IdVariante",
                table: "RigheCarrello",
                columns: new[] { "IdTenant", "IdSito", "IdVariante" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RigheCarrello");

            migrationBuilder.DropTable(
                name: "Carrelli");
        }
    }
}
