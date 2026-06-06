using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantAgent.API.Migrations
{
    /// <inheritdoc />
    public partial class Order38_DataAnomaly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "data_anomaly",
                table: "predicciones",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "data_anomaly",
                table: "predicciones");
        }
    }
}
