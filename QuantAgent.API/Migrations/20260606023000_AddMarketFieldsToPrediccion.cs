using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantAgent.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketFieldsToPrediccion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "corners_over_under",
                table: "predicciones",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "mercado",
                table: "predicciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "total_goals",
                table: "predicciones",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "corners_over_under",
                table: "predicciones");

            migrationBuilder.DropColumn(
                name: "mercado",
                table: "predicciones");

            migrationBuilder.DropColumn(
                name: "total_goals",
                table: "predicciones");
        }
    }
}
