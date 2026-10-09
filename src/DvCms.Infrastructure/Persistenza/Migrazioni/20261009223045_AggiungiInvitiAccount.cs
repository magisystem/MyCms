using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiInvitiAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvitiAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HashToken = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ScadeIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatoDaIdUtenza = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccettatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitiAccount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvitiAccount_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvitiAccount_Utenze_CreatoDaIdUtenza",
                        column: x => x.CreatoDaIdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvitiAccount_Utenze_IdUtenza",
                        column: x => x.IdUtenza,
                        principalTable: "Utenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvitiAccount_CreatoDaIdUtenza",
                table: "InvitiAccount",
                column: "CreatoDaIdUtenza");

            migrationBuilder.CreateIndex(
                name: "IX_InvitiAccount_HashToken",
                table: "InvitiAccount",
                column: "HashToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvitiAccount_IdTenant",
                table: "InvitiAccount",
                column: "IdTenant");

            migrationBuilder.CreateIndex(
                name: "IX_InvitiAccount_IdUtenza_Stato",
                table: "InvitiAccount",
                columns: new[] { "IdUtenza", "Stato" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvitiAccount");
        }
    }
}
