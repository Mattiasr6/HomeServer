using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantAgent.API.Migrations
{
    /// <inheritdoc />
    public partial class _20260605_InitialQuantSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "partidos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo_local = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    equipo_visitante = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fecha_inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    goles_local = table.Column<int>(type: "integer", nullable: true),
                    goles_visitante = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partidos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reglas_aprendidas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contexto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    regla = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    peso = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reglas_aprendidas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "predicciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seleccion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cuota = table.Column<decimal>(type: "numeric(10,3)", nullable: false),
                    confianza = table.Column<int>(type: "integer", nullable: false),
                    razonamiento = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_predicciones", x => x.id);
                    table.ForeignKey(
                        name: "FK_predicciones_partidos_partido_id",
                        column: x => x.partido_id,
                        principalTable: "partidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_partidos_estado",
                table: "partidos",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_partidos_fecha_inicio",
                table: "partidos",
                column: "fecha_inicio");

            migrationBuilder.CreateIndex(
                name: "ix_predicciones_estado",
                table: "predicciones",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_predicciones_partido_id",
                table: "predicciones",
                column: "partido_id");

            migrationBuilder.CreateIndex(
                name: "ix_reglas_aprendidas_equipo",
                table: "reglas_aprendidas",
                column: "equipo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "predicciones");

            migrationBuilder.DropTable(
                name: "reglas_aprendidas");

            migrationBuilder.DropTable(
                name: "partidos");
        }
    }
}
