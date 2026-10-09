using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.Infrastructure.Persistenza.Migrazioni
{
    /// <inheritdoc />
    public partial class AggiungiOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessaggiOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChiaveIdempotenza = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Stato = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tentativi = table.Column<int>(type: "int", nullable: false),
                    ProssimoTentativoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BloccatoFinoAlUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BloccatoDa = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UltimoErrore = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ElaboratoIlUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessaggiOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessaggiOutbox_Tenant_IdTenant",
                        column: x => x.IdTenant,
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessaggiOutbox_Idempotenza",
                table: "MessaggiOutbox",
                columns: new[] { "IdTenant", "ChiaveIdempotenza" },
                unique: true,
                filter: "[ChiaveIdempotenza] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessaggiOutbox_Stato_ProssimoTentativoIlUtc",
                table: "MessaggiOutbox",
                columns: new[] { "Stato", "ProssimoTentativoIlUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessaggiOutbox");
        }
    }
}
