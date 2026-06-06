using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantAgent.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPrediccionIdToReglasAprendidas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "prediccion_id",
                table: "reglas_aprendidas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_reglas_aprendidas_prediccion_id",
                table: "reglas_aprendidas",
                column: "prediccion_id");

            migrationBuilder.AddForeignKey(
                name: "FK_reglas_aprendidas_predicciones_prediccion_id",
                table: "reglas_aprendidas",
                column: "prediccion_id",
                principalTable: "predicciones",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reglas_aprendidas_predicciones_prediccion_id",
                table: "reglas_aprendidas");

            migrationBuilder.DropIndex(
                name: "ix_reglas_aprendidas_prediccion_id",
                table: "reglas_aprendidas");

            migrationBuilder.DropColumn(
                name: "prediccion_id",
                table: "reglas_aprendidas");
        }
    }
}
