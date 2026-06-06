using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantAgent.API.Migrations
{
    /// <inheritdoc />
    public partial class AddStakeSugeridoToPredicciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StakeSugerido",
                table: "predicciones",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StakeSugerido",
                table: "predicciones");
        }
    }
}
