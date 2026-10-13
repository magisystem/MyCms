using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiAnonimizzazioneDatiPersonali : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnonimizzatoIlUtc",
                table: "Ordini",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ordini_AggiornatoIlUtc",
                table: "Ordini",
                column: "AggiornatoIlUtc",
                filter: "[AnonimizzatoIlUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ordini_AggiornatoIlUtc",
                table: "Ordini");

            migrationBuilder.DropColumn(
                name: "AnonimizzatoIlUtc",
                table: "Ordini");
        }
    }
}
