using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiDueFattori : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueFattoriAttivatiIlUtc",
                table: "Utenze",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DueFattoriAttivi",
                table: "Utenze",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SegretoTotpInAttesaProtetto",
                table: "Utenze",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SegretoTotpProtetto",
                table: "Utenze",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UltimoPassoTotp",
                table: "Utenze",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "CodiciRecuperoUtenza",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HashCodice = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodiciRecuperoUtenza", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodiciRecuperoUtenza_Utenze_IdUtenza",
                        column: x => x.IdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodiciRecuperoUtenza_IdUtenza_HashCodice",
                table: "CodiciRecuperoUtenza",
                columns: new[] { "IdUtenza", "HashCodice" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodiciRecuperoUtenza");

            migrationBuilder.DropColumn(
                name: "DueFattoriAttivatiIlUtc",
                table: "Utenze");

            migrationBuilder.DropColumn(
                name: "DueFattoriAttivi",
                table: "Utenze");

            migrationBuilder.DropColumn(
                name: "SegretoTotpInAttesaProtetto",
                table: "Utenze");

            migrationBuilder.DropColumn(
                name: "SegretoTotpProtetto",
                table: "Utenze");

            migrationBuilder.DropColumn(
                name: "UltimoPassoTotp",
                table: "Utenze");
        }
    }
}
