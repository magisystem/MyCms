using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiPagine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pagine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdGruppoTraduzione = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TipoPagina = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaginaIniziale = table.Column<bool>(type: "bit", nullable: false),
                    IdUltimaRevisioneBozza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdRevisionePubblicata = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UltimoNumeroRevisione = table.Column<int>(type: "int", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AggiornatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PubblicatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TokenConcorrenza = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagine", x => x.Id);
                    table.UniqueConstraint("AK_Pagine_IdTenant_IdSito_Id", x => new { x.IdTenant, x.IdSito, x.Id });
                    table.CheckConstraint("CK_Pagine_SlugMinuscolo", "[Slug] COLLATE Latin1_General_BIN2 = LOWER([Slug]) COLLATE Latin1_General_BIN2");
                    table.ForeignKey(
                        name: "FK_Pagine_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pagine_Siti_IdTenant_IdSito",
                        columns: x => new { x.IdTenant, x.IdSito },
                        principalTable: "Siti",
                        principalColumns: new[] { "IdTenant", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RevisioniPagina",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdPagina = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NumeroRevisione = table.Column<int>(type: "int", nullable: false),
                    VersioneSchema = table.Column<int>(type: "int", nullable: false),
                    Titolo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Contenuto = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NotaRevisione = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisioniPagina", x => x.Id);
                    table.UniqueConstraint("AK_RevisioniPagina_IdPagina_Id", x => new { x.IdPagina, x.Id });
                    table.ForeignKey(
                        name: "FK_RevisioniPagina_Pagine_IdTenant_IdSito_IdPagina",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdPagina },
                        principalTable: "Pagine",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevisioniPagina_Utenze_CreatoDaIdUtenza",
                        column: x => x.CreatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pagine_Id_IdRevisionePubblicata",
                table: "Pagine",
                columns: new[] { "Id", "IdRevisionePubblicata" });

            migrationBuilder.CreateIndex(
                name: "IX_Pagine_Id_IdUltimaRevisioneBozza",
                table: "Pagine",
                columns: new[] { "Id", "IdUltimaRevisioneBozza" });

            migrationBuilder.CreateIndex(
                name: "IX_Pagine_IdLingua",
                table: "Pagine",
                column: "IdLingua");

            migrationBuilder.CreateIndex(
                name: "IX_Pagine_IdSito_IdLingua_Slug",
                table: "Pagine",
                columns: new[] { "IdSito", "IdLingua", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pagine_PaginaIniziale",
                table: "Pagine",
                columns: new[] { "IdSito", "IdLingua" },
                unique: true,
                filter: "[PaginaIniziale] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RevisioniPagina_CreatoDaIdUtenza",
                table: "RevisioniPagina",
                column: "CreatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_RevisioniPagina_IdPagina_NumeroRevisione",
                table: "RevisioniPagina",
                columns: new[] { "IdPagina", "NumeroRevisione" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevisioniPagina_IdTenant_IdSito_IdPagina",
                table: "RevisioniPagina",
                columns: new[] { "IdTenant", "IdSito", "IdPagina" });

            migrationBuilder.AddForeignKey(
                name: "FK_Pagine_RevisioniPagina_Id_IdRevisionePubblicata",
                table: "Pagine",
                columns: new[] { "Id", "IdRevisionePubblicata" },
                principalTable: "RevisioniPagina",
                principalColumns: new[] { "IdPagina", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pagine_RevisioniPagina_Id_IdUltimaRevisioneBozza",
                table: "Pagine",
                columns: new[] { "Id", "IdUltimaRevisioneBozza" },
                principalTable: "RevisioniPagina",
                principalColumns: new[] { "IdPagina", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pagine_RevisioniPagina_Id_IdRevisionePubblicata",
                table: "Pagine");

            migrationBuilder.DropForeignKey(
                name: "FK_Pagine_RevisioniPagina_Id_IdUltimaRevisioneBozza",
                table: "Pagine");

            migrationBuilder.DropTable(
                name: "RevisioniPagina");

            migrationBuilder.DropTable(
                name: "Pagine");
        }
    }
}
