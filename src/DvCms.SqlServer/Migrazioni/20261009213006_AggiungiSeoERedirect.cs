using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiSeoERedirect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescrizioneSeo",
                table: "RevisioniPagina",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NonIndicizzare",
                table: "RevisioniPagina",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TitoloSeo",
                table: "RevisioniPagina",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PubblicataInPassato",
                table: "Pagine",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Le pagine già pubblicate hanno un indirizzo pubblico: un loro cambio di slug richiederà il reindirizzamento.
            migrationBuilder.Sql("UPDATE [Pagine] SET [PubblicataInPassato] = 1 WHERE [IdRevisionePubblicata] IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "RedirectPagine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdSito = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdLingua = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlugOrigine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdPagina = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedirectPagine", x => x.Id);
                    table.CheckConstraint("CK_RedirectPagine_SlugMinuscolo", "[SlugOrigine] COLLATE Latin1_General_BIN2 = LOWER([SlugOrigine]) COLLATE Latin1_General_BIN2");
                    table.ForeignKey(
                        name: "FK_RedirectPagine_Lingue_IdLingua",
                        column: x => x.IdLingua,
                        principalTable: "Lingue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RedirectPagine_Pagine_IdTenant_IdSito_IdPagina",
                        columns: x => new { x.IdTenant, x.IdSito, x.IdPagina },
                        principalTable: "Pagine",
                        principalColumns: new[] { "IdTenant", "IdSito", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RedirectPagine_IdLingua",
                table: "RedirectPagine",
                column: "IdLingua");

            migrationBuilder.CreateIndex(
                name: "IX_RedirectPagine_IdPagina",
                table: "RedirectPagine",
                column: "IdPagina");

            migrationBuilder.CreateIndex(
                name: "IX_RedirectPagine_IdSito_IdLingua_SlugOrigine",
                table: "RedirectPagine",
                columns: new[] { "IdSito", "IdLingua", "SlugOrigine" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RedirectPagine_IdTenant_IdSito_IdPagina",
                table: "RedirectPagine",
                columns: new[] { "IdTenant", "IdSito", "IdPagina" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RedirectPagine");

            migrationBuilder.DropColumn(
                name: "DescrizioneSeo",
                table: "RevisioniPagina");

            migrationBuilder.DropColumn(
                name: "NonIndicizzare",
                table: "RevisioniPagina");

            migrationBuilder.DropColumn(
                name: "TitoloSeo",
                table: "RevisioniPagina");

            migrationBuilder.DropColumn(
                name: "PubblicataInPassato",
                table: "Pagine");
        }
    }
}
