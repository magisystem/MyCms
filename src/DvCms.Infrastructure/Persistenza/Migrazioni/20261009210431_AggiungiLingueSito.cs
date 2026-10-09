using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiLingueSito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LingueSito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Predefinita = table.Column<bool>(type: "bit", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LingueSito", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LingueSito_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LingueSito_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LingueSito_IdLingua",
                table: "LingueSito",
                column: "IdLingua");

            migrationBuilder.CreateIndex(
                name: "IX_LingueSito_IdSito_IdLingua",
                table: "LingueSito",
                columns: new[] { "IdSito", "IdLingua" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LingueSito_IdTenant_IdSito",
                table: "LingueSito",
                columns: new[] { "IdTenant", "IdSito" });

            migrationBuilder.CreateIndex(
                name: "IX_LingueSito_Predefinita",
                table: "LingueSito",
                column: "IdSito",
                unique: true,
                filter: "[Predefinita] = 1");

            // I siti già esistenti parlano italiano: lingua predefinita unica (l'identificativo è quello fisso di Lingua.IdItaliano).
            migrationBuilder.Sql(
                """
                INSERT INTO LingueSito (Id, IdTenant, IdSito, IdLingua, Predefinita, CreatoIlUtc)
                SELECT NEWID(), s.IdTenant, s.Id, '01996a00-0000-7000-8000-000000000001', 1, SYSUTCDATETIME()
                FROM Siti s
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LingueSito");
        }
    }
}
