using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantAgent.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add CHECK constraint: confianza must be 0-100
            migrationBuilder.Sql(
                "ALTER TABLE predicciones ADD CONSTRAINT ck_predicciones_confianza CHECK (confianza >= 0 AND confianza <= 100);");

            // Add CHECK constraint: cuota must be positive and <= 20.00
            migrationBuilder.Sql(
                "ALTER TABLE predicciones ADD CONSTRAINT ck_predicciones_cuota CHECK (cuota > 0 AND cuota <= 20.00);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE predicciones DROP CONSTRAINT IF EXISTS ck_predicciones_confianza;");
            migrationBuilder.Sql("ALTER TABLE predicciones DROP CONSTRAINT IF EXISTS ck_predicciones_cuota;");
        }
    }
}
