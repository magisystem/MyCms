using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiRegistrazioneAcquirenti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfermeEmailUtenza",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HashToken = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ScadeIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfermataIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfermeEmailUtenza", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfermeEmailUtenza_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfermeEmailUtenza_Utenze_IdUtenza",
                        column: x => x.IdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfermeEmailUtenza_HashToken",
                table: "ConfermeEmailUtenza",
                column: "HashToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfermeEmailUtenza_IdTenant",
                table: "ConfermeEmailUtenza",
                column: "IdTenant");

            migrationBuilder.CreateIndex(
                name: "IX_ConfermeEmailUtenza_IdUtenza_CreatoIlUtc",
                table: "ConfermeEmailUtenza",
                columns: new[] { "IdUtenza", "CreatoIlUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfermeEmailUtenza");
        }
    }
}
