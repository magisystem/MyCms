using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DvCms.SqlServer.Migrazioni
{
    /// <inheritdoc />
    public partial class RimborsiEsterni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rimborsi_IdSito_IdProvider",
                table: "Rimborsi");

            migrationBuilder.AlterColumn<Guid>(
                name: "IdUtenzaRichiedente",
                table: "Rimborsi",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<bool>(
                name: "Esterno",
                table: "Rimborsi",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Rimborsi_IdSito_IdProvider",
                table: "Rimborsi",
                columns: new[] { "IdSito", "IdProvider" },
                unique: true,
                filter: "[IdProvider] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rimborsi_IdSito_IdProvider",
                table: "Rimborsi");

            migrationBuilder.DropColumn(
                name: "Esterno",
                table: "Rimborsi");

            migrationBuilder.AlterColumn<Guid>(
                name: "IdUtenzaRichiedente",
                table: "Rimborsi",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rimborsi_IdSito_IdProvider",
                table: "Rimborsi",
                columns: new[] { "IdSito", "IdProvider" },
                filter: "[IdProvider] IS NOT NULL");
        }
    }
}
